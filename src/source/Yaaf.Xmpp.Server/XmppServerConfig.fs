﻿// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
module Yaaf.Xmpp.Server.XmppServerConfig

open FSharp.Configuration
open Yaaf.Xmpp.Runtime
open System.Collections.Generic
open System.Data.Common
open System.Data.Entity

open Yaaf.Helper
open Yaaf.Logging
open Yaaf.Xmpp.IM.Server
open Yaaf.Xmpp.IM.Sql

open Yaaf.Xmpp.MessageArchiving.Server
open Yaaf.Xmpp.MessageArchiveManager.Sql
open Yaaf.Xmpp.MessageArchiveManager.IMAP
open Yaaf.Xmpp.MessageArchiving
open Yaaf.Sasl.Ldap
open Yaaf.Database
open Yaaf.Xmpp
open Yaaf.Xmpp.MessageArchiveManager

type ConfigFile = YamlConfig<"Config.yaml">

type ConnectionString =
  { Raw : string
    Parsed : IDictionary<string, string> }

let ParseConnectionString connStr = 
    let builder = DbConnectionStringBuilder ()
    builder.ConnectionString <- connStr
    let parsedDict =
      builder :> System.Collections.IDictionary 
        |> Seq.cast |> Seq.map (fun (kv:KeyValuePair<string, obj>) -> kv.Key, kv.Value :?> string) 
        |> dict
    { Raw =  connStr; Parsed = parsedDict  }

let GetCertificate (cert: ConfigFile.Certificate_Type) =
    ServerCertificateData.OpenSslFiles(cert.Private, cert.Public, cert.Password)


let GetComponents (components:ConfigFile.Components_Item_Type seq) = 
    components |> Seq.map (fun comp -> 
        { Subdomain = comp.Domain; Secret = comp.Secret })
        |> Seq.toList

let GetServerSaslMechanism (auths : ConfigFile.Authentication_Item_Type seq) =
    auths |> Seq.map (fun auth ->
      match auth.Type with
      | "ldap" ->
        let con = ParseConnectionString auth.ConnectionString
        let server = con.Parsed.["server"]
        let port = System.Int32.Parse con.Parsed.["port"]
        let ssl = System.Boolean.Parse con.Parsed.["ssl"]
        let source =
          LdapUserSource({HostName = server; Port = port; Ssl = ssl}, fun username ->
              System.String.Format(auth.MapUserId, username))
        new Yaaf.Sasl.Plain.PlainServer(source) :> Yaaf.Sasl.IServerMechanism
      | _ ->
        failwithf "Unsupported authentication type %s (only 'ldap' is currently supported)" auth.Type)
      |> Seq.toList

let resourceManager =
    { new Features.IResourceManager with
        member x.IsConnected jid = async.Return false
        member x.Disconnect jid = async.Return()
        member x.GenerateResourceId jid = { jid with Resource = Some(System.Guid.NewGuid().ToString()) } }

type internal MyDB = MyDB
type ConnectionStore<'T>() =
    static let mutable connection = null : string
    static member Connection with get() = connection and set v = connection <- v
module ConnectionStore =
    let Create<'T when 'T : (new : unit -> 'T) and 'T :> DbContext> (connectionString) =
        if ConnectionStore<'T>.Connection <> null then
          failwith "multiple connections to the same database type are not supported"
        ConnectionStore<'T>.Connection <- connectionString
        ( 
          Log.Info(fun () -> L "Initialize Database %s (connected via: %s)" (typeof<'T>.Name) connectionString)
          use c =
            try
              new 'T()
            with :? System.Reflection.TargetInvocationException as e ->
              Async.reraise e.InnerException

          c.Database.Initialize(false)
          c.SaveChanges() |> ignore
        )
        fun () -> new 'T()
type MySqlHistoryContext (con, scheme) =
    inherit System.Data.Entity.Migrations.History.HistoryContext(con, scheme)

    override x.OnModelCreating(modelBuilder) = 
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<System.Data.Entity.Migrations.History.HistoryRow>()
          .Property(fun h -> h.MigrationId).HasMaxLength(System.Nullable(100)).IsRequired()
          |> ignore
        modelBuilder.Entity<System.Data.Entity.Migrations.History.HistoryRow>()
          .Property(fun h -> h.ContextKey).HasMaxLength(System.Nullable(200)).IsRequired()
          |> ignore

type MSSQLRosterDatabase<'T> () = 
    inherit AbstractRosterStoreDbContext(ConnectionStore<MSSQLRosterDatabase<'T>>.Connection)

type MySQLConfiguration<'T when 'T :> DbContext> () as x =
    inherit System.Data.Entity.Migrations.DbMigrationsConfiguration<'T> ()
    do
      x.CodeGenerator <- new MySql.Data.Entity.MySqlMigrationCodeGenerator()
      x.SetSqlGenerator("MySql.Data.MySqlClient", new MySql.Data.Entity.MySqlMigrationSqlGenerator ())
      x.SetHistoryContextFactory("MySql.Data.MySqlClient", fun conn schema -> new MySqlHistoryContext(conn, schema) :> _)

      x.AutomaticMigrationsEnabled <- true
    override x.Seed(context) = ()

type [<DbConfigurationType (typeof<MySql.Data.Entity.MySqlEFConfiguration>)>] MySQLRosterDatabase<'T> () = 
    inherit AbstractRosterStoreDbContext(ConnectionStore<MySQLRosterDatabase<'T>>.Connection)
    override x.Init () = 
        DbConfiguration.SetConfiguration (new MySql.Data.Entity.MySqlEFConfiguration ())
        System.Data.Entity.Database.SetInitializer<MySQLRosterDatabase<'T>> (
            new MigrateDatabaseToLatestVersion<MySQLRosterDatabase<'T>, MySQLConfiguration<MySQLRosterDatabase<'T>>> ())


let CreateRosterStore (stores : ConfigFile.RosterStore_Item_Type seq) =
    let rosterStores =
        stores |> Seq.map (fun store ->
          match store.Type with
          | "mssql" ->
            let creator = ConnectionStore.Create<MSSQLRosterDatabase<MyDB>> store.ConnectionString
            let sqlStore = new SqlRosterStore(fun () -> creator() :> AbstractRosterStoreDbContext)
            ConcurrentRosterStore(sqlStore) :> IRosterStore
          | "mysql" ->
            let creator = ConnectionStore.Create<MySQLRosterDatabase<MyDB>> store.ConnectionString
            let sqlStore = new SqlRosterStore(fun () -> creator() :> AbstractRosterStoreDbContext)
            ConcurrentRosterStore(sqlStore) :> IRosterStore
          | _ ->
            failwithf "Unsupported roster store type %s (only 'mssql' and 'mysql' are currently supported)" store.Type)
          |> Seq.toList
    match rosterStores with
    | h :: [] -> h
    | _ ->
      failwith "defining multiple roster stores is not supported."
   
type MSSQLMessageArchiveDatabase<'T> () = 
    inherit AbstractMessageArchivingDbContext(ConnectionStore<MSSQLMessageArchiveDatabase<'T>>.Connection)


and [<DbConfigurationType (typeof<MySql.Data.Entity.MySqlEFConfiguration>)>] MySQLMessageArchiveDatabase<'T> () = 
    inherit AbstractMessageArchivingDbContext(ConnectionStore<MySQLMessageArchiveDatabase<'T>>.Connection)
    override x.Init () = 
        DbConfiguration.SetConfiguration (new MySql.Data.Entity.MySqlEFConfiguration ())
        System.Data.Entity.Database.SetInitializer<MySQLMessageArchiveDatabase<'T>> (
            new MigrateDatabaseToLatestVersion<MySQLMessageArchiveDatabase<'T>, MySQLConfiguration<MySQLMessageArchiveDatabase<'T>>> ())
 
// Hack because we do not have this implemented properly in the backends for now.
type ReplacePreferenceStoreWithMemory (defStore : Yaaf.Xmpp.MessageArchiving.IMessageArchivingStore) = 
    let userPrefStore = new System.Collections.Concurrent.ConcurrentDictionary<_,IUserPreferenceStore>() // concurrent dictionary!
    //let userColStore = new System.Collections.Generic.Dictionary<_, _>() // concurrent dictionary!
    interface System.IDisposable with
        member x.Dispose() =
              try
                match defStore with
                | :? System.IDisposable as d -> d.Dispose()
                | _ -> ()
              with exn ->
                  System.Console.Error.WriteLine(sprintf "Error on dispose: %O" exn)

    interface IMessageArchivingStore with
        member x.GetPreferenceStore (jid:JabberId) = // defStore.GetPreferenceStore jid
            userPrefStore.GetOrAdd(jid.BareId, (fun k ->
                let newStore = MemoryUserPreferenceStore(jid.BareJid) :> IUserPreferenceStore
                newStore))
            |> Task.FromResult

        member x.GetArchiveStore (jid:JabberId) = defStore.GetArchiveStore jid

   
let CreateMessageArchiveStore (stores : ConfigFile.MessageArchive_Item_Type seq) =
    let messageStores =
        stores |> Seq.map (fun store ->
          let ret (backendStore:IMessageArchivingStore) = 
            if store.Writeonly then
              if store.ReplacePreferenceStoreWithMemory then failwith "cannot define Writeonly and ReplacePreferenceStoreWithMemory at the same time!"
              Choice1Of2 (ArchivingStore.WriteOnly(backendStore))
            else
              Choice2Of2 
                (if store.ReplacePreferenceStoreWithMemory then
                   new ReplacePreferenceStoreWithMemory (backendStore) :> IMessageArchivingStore
                 else backendStore)
          match store.Type with
          | "mssql" ->
            let creator = ConnectionStore.Create<MSSQLMessageArchiveDatabase<MyDB>> store.ConnectionString
            let sqlStore = new MessageArchivingStore(fun () -> creator() :> AbstractMessageArchivingDbContext)
            ret (sqlStore :> IMessageArchivingStore)
          | "mysql" ->
            let creator = ConnectionStore.Create<MySQLMessageArchiveDatabase<MyDB>> store.ConnectionString
            let sqlStore = new MessageArchivingStore(fun () -> creator() :> AbstractMessageArchivingDbContext)
            ret (sqlStore :> IMessageArchivingStore)
          | "imap" ->
            if not store.Writeonly then failwith "imap can not be used to read from!"
            let con = ParseConnectionString store.ConnectionString
            let server = con.Parsed.["server"]
            let user = con.Parsed.["uid"]
            let pwd = con.Parsed.["pwd"]
            let folder = con.Parsed.["folder"]
            let timeout = 
              if con.Parsed.ContainsKey "timeout" then
                System.Int32.Parse con.Parsed.["timeout"]
              else 5000
            let wrapper = SimpleWrapper(ActiveUpImap4.createProcessor(), timeout)

            let imap = ImapConnection(wrapper, server, user, pwd)
            { new IMessageArchivingStore with
                member x.GetPreferenceStore jid = failwith "imap doens't support saving settings"
                member x.GetArchiveStore jid =
                  let folderPath = System.String.Format(folder, jid.Localpart.Value)
                  let user = jid.BareJid
                  
                  ImapArchiveManager.convertImapToArchiveManager 
                      folderPath (GoogleChat.MessageMapper.createMessageMapper user) user imap
                  :> IUserArchivingStore
                  |> async.Return |> Async.StartAsTask }
            |> ArchivingStore.WriteOnly
            |> Choice1Of2
          | _ ->
            failwithf "Unsupported roster store type %s (only 'mssql' and 'mysql' are currently supported)" store.Type)
          |> Seq.toList
    let writeOnlyStores =
      messageStores |> List.choose (function Choice1Of2 s -> Some s | _ -> None)
    let normalStores =
      messageStores |> List.choose (function Choice2Of2 s -> Some s | _ -> None)
    let normalStore =
      match normalStores with
      | n :: [] -> n
      | _ -> failwith "you cannot have multiple message archives with read access!"
    ArchivingStore.Combine(writeOnlyStores, normalStore)

let CreateXmppSetup (config:ConfigFile) =

    XmppServerSetup.CreateDefault config.Domain
    |> XmppServerSetup.addServerCore 
      (GetComponents config.Components) (Some (GetCertificate config.Certificate))
      resourceManager (GetServerSaslMechanism config.Authentication)
    |> XmppServerSetup.addXmppServerPlugin
    |> XmppServerSetup.addPerUserService
    |> XmppServerSetup.addDiscoPlugin
    |> XmppServerSetup.addIMPlugin 
        { ImServerConfig.Default with RosterStore = CreateRosterStore config.RosterStore }
    |> XmppServerSetup.addMessageArchivingPlugin 
        { MessageArchivingServerPluginConfig.Default with MessageArchiveStore = CreateMessageArchiveStore config.MessageArchive }

let CreatePortConfig (serverPorts:ConfigFile.ServerPorts_Item_Type seq) =
   serverPorts |> Seq.map (fun portConfig ->
      match portConfig.Type with
      | "s2s" -> (StreamType.ServerStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | "c2s" -> (StreamType.ClientStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | "component" ->
        ((StreamType.ComponentStream true), (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | _ -> failwithf "unknown port type %s" portConfig.Type
   )

let StartFromConfig (config:ConfigFile) =
    let xmppServer = XmppServer(CreateXmppSetup config)
    
    xmppServer.Errors 
        |> Event.add 
            (fun e -> 
                let errMsg = sprintf "XmppServerTestClass: Server-Client faulted: %O" e
                System.Console.Error.WriteLine(errMsg)
                Log.Err (fun () -> errMsg))

    for streamType, endPoint in CreatePortConfig config.ServerPorts do
      xmppServer.StartListen (streamType, endPoint) 
          |> ignore
    xmppServer