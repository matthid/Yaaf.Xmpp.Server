// ----------------------------------------------------------------------------
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

open Yaaf.Xmpp.VCard.Server
open Yaaf.Xmpp.MessageArchiving.Server
open Yaaf.Xmpp.MessageArchiveManager.Sql
open Yaaf.Xmpp.MessageArchiveManager.IMAP
open Yaaf.Xmpp.MessageArchiving
open Yaaf.Sasl.Ldap
open Yaaf.Database
open Yaaf.Xmpp
open Yaaf.Xmpp.MessageArchiveManager
open Yaaf.Xmpp.IM.Sql.MySql 
open Yaaf.Xmpp.MessageArchiveManager.Sql.MySql

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
    let Create<'T when 'T :> DbContext and 'T :> IUpgradeDatabaseProvider> 
      deleteDatabase (connectionString) =
        if ConnectionStore<'T>.Connection <> null then
          failwith "multiple connections to the same database type are not supported"
        ConnectionStore<'T>.Connection <- connectionString
        let creator = (fun () -> System.Activator.CreateInstance(typeof<'T>,  [| connectionString :> obj; false :> obj |]) :?> 'T)
        (
          Database.SetInitializer<'T>(null)
          use c =
            try
              creator () 
            with :? System.Reflection.TargetInvocationException as e ->
              Async.reraise e.InnerException
          if deleteDatabase then
            c.Database.Delete() |> ignore
            c.SaveChanges() |> ignore
          else
            c.Upgrade()
        )
        creator
        
/// We need this because EF will not allow us to force init otherwise: http://stackoverflow.com/questions/19430502/dropcreatedatabaseifmodelchanges-ef6-results-in-system-invalidoperationexception
type MSSQLRosterDatabase (connection, doInit) = 
    inherit MSSQLRosterStoreDbContext(connection, doInit)
/// We need this because EF will not allow us to force init otherwise: http://stackoverflow.com/questions/19430502/dropcreatedatabaseifmodelchanges-ef6-results-in-system-invalidoperationexception
type MySQLRosterDatabase (connection, doInit) = 
    inherit MySqlRosterStoreDbContext(connection, doInit)

let CreateRosterStore deleteDatabase (stores : ConfigFile.RosterStore_Item_Type seq) =
    let rosterStores =
        stores |> Seq.map (fun store ->
          Log.Info(fun () -> L "Initialize Roster Database %s (connected via: %s)" store.Type store.ConnectionString) 
          match store.Type with
          | "mssql" ->
            let creator = ConnectionStore.Create<MSSQLRosterDatabase> deleteDatabase store.ConnectionString
            let sqlStore = new SqlRosterStore(fun () -> creator() :> AbstractRosterStoreDbContext)
            ConcurrentRosterStore(sqlStore) :> IRosterStore
          | "mysql" ->
            let creator = ConnectionStore.Create<MySQLRosterDatabase> deleteDatabase store.ConnectionString
            let sqlStore = new SqlRosterStore(fun () -> creator() :> AbstractRosterStoreDbContext)
            ConcurrentRosterStore(sqlStore) :> IRosterStore
          | _ ->
            failwithf "Unsupported roster store type %s (only 'mssql' and 'mysql' are currently supported)" store.Type)
          |> Seq.toList
    match rosterStores with
    | h :: [] -> h
    | _ ->
      failwith "defining multiple roster stores is not supported."

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

/// We need this because EF will not allow us to force init otherwise: http://stackoverflow.com/questions/19430502/dropcreatedatabaseifmodelchanges-ef6-results-in-system-invalidoperationexception
type MSSQLMessageArchiveDatabase (connection, doInit) = 
    inherit MSSQLMessageArchivingDbContext(connection, doInit)
    
/// We need this because EF will not allow us to force init otherwise: http://stackoverflow.com/questions/19430502/dropcreatedatabaseifmodelchanges-ef6-results-in-system-invalidoperationexception
and MySQLMessageArchiveDatabase (connection, doInit)  = 
    inherit MySqlArchiveManagerDbContext(connection, doInit)
   
let CreateMessageArchiveStore deleteDatabase (stores : ConfigFile.MessageArchive_Item_Type seq) =
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
          Log.Info(fun () -> L "Initialize MessageArchive Database %s (connected via: %s)" store.Type store.ConnectionString)
          match store.Type with
          | "mssql" ->
            let creator = ConnectionStore.Create<MSSQLMessageArchiveDatabase> deleteDatabase store.ConnectionString
            let sqlStore = new MessageArchivingStore(fun () -> creator() :> AbstractMessageArchivingDbContext)
            ret (sqlStore :> IMessageArchivingStore)
          | "mysql" ->
            let creator = ConnectionStore.Create<MySQLMessageArchiveDatabase> deleteDatabase store.ConnectionString
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

let GetVCardConfig () = 
    { VCardSource = 
        { new IVCardSource with
            member x.GetVCard i = 
              System.Threading.Tasks.Task.FromResult
                (Some ( VCard.VCardInfo.Create(i.Localpart.Value, VCard.VCardName.Create()))) 
            member x.SetVCard (i, v) = 
              async {
                raise <| new System.NotSupportedException("not implemented")
              } |> Async.StartAsTask :> _
        }
    }

let internal SetVCardConfig (setup, config:IVCardConfig) =
    XmppSetup.SetConfig<VCardConfig,_> (setup, config)
let internal setVCardConfig config setup = SetVCardConfig(setup, config)
let AddVCard (setup: ClientSetup, config) = 
    let setupVCard (runtime:XmppRuntime) = 
        let mgr = runtime.PluginManager
        mgr.RegisterPlugin<VCardServerPlugin>()
    setup
    |> setVCardConfig config
    |> XmppSetup.addHelper ignore setupVCard
let addVCard config setup = AddVCard(setup, config)

let CreateXmppSetup deleteDatabase (config:ConfigFile) =
    let rosterStore, archiveStore =
      try
        let rosterStore = CreateRosterStore deleteDatabase config.RosterStore
        let archiveStore = CreateMessageArchiveStore deleteDatabase config.MessageArchive
        if deleteDatabase then
          // normally we don't want to use exceptions for control flow,
          // however as --force is already a workaround (and should never be used in production)
          // we will just not continue from this point.
          failwith "Database created, please run now without --deleteDatabase"
        else rosterStore, archiveStore
      with
      | :? DatabaseUpgradeException as e ->
        Log.Err(fun () -> L "Database failed to upgrade: %O" e)
        let scriptFileOrErrorMessage =
          if e.ScriptGenerationException <> null then e.Message
          else
            let file = System.IO.Path.GetFullPath("upgrade.sql")
            System.IO.File.WriteAllText(file, e.UpgradeScript)
            file
        failwithf """
--- PLEASE READ ---
--- PLEASE READ ---
--- PLEASE READ ---

We tried to upgrade your database but failed!
This is a good time to quickly make a database backup before proceeding.
If you use MySQL the problem is most likely a bug in MySQL Connector/NET.
To help you to get out of this situation you have various options:

 * THIS WILL DELETE YOUR CURRENT DATABASE, ALL YOUR DATA WILL BE LOST.
   You can try to re-create the databas with --deleteDatabase.
 * We provide you with an initial script and you can try to do the upgrade manually.
   You can find the script here (note that it needs to be fixed manually before applying): %s
 * If the bug is known we probably already provide a working script for you.
   Please try to find it on either on https://matthid.github.io/Yaaf.Xmpp.IM.SQL/ or https://matthid.github.io/Yaaf.MessageArchiveManager.SQL/
   depending on which database is failing to upgrade.

Additional Information:
%O

If the Script could not get generated here is more Information:
%O
        """ scriptFileOrErrorMessage e e.ScriptGenerationException

    XmppServerSetup.CreateDefault config.Domain
    |> XmppServerSetup.addServerCore 
      (GetComponents config.Components) (Some (GetCertificate config.Certificate))
      resourceManager (GetServerSaslMechanism config.Authentication)
    |> XmppServerSetup.addXmppServerPlugin
    |> XmppServerSetup.addPerUserService
    |> XmppServerSetup.addDiscoPlugin
    |> XmppServerSetup.addToAllStreams (addVCard (GetVCardConfig()))
    |> XmppServerSetup.addIMPlugin
        { ImServerConfig.Default with RosterStore = rosterStore }
    |> XmppServerSetup.addMessageArchivingPlugin 
        { MessageArchivingServerPluginConfig.Default with MessageArchiveStore = archiveStore }


let CreatePortConfig (serverPorts:ConfigFile.ServerPorts_Item_Type seq) =
   serverPorts |> Seq.map (fun portConfig ->
      match portConfig.Type with
      | "s2s" -> (StreamType.ServerStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | "c2s" -> (StreamType.ClientStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | "component" ->
        ((StreamType.ComponentStream true), (System.Net.IPEndPoint(System.Net.IPAddress.Any, portConfig.Port)))
      | _ -> failwithf "unknown port type %s" portConfig.Type
   )

let StartFromConfig deleteDatabase (config:ConfigFile) =
    let xmppServer = XmppServer(CreateXmppSetup deleteDatabase config)
    
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