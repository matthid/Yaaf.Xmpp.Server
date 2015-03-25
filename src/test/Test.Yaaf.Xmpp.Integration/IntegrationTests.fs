// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Test.Yaaf.Xmpp.Integration
open FsUnit
open NUnit.Framework
open Yaaf.Helper
open System.Collections.Generic
open Yaaf.TestHelper
open Yaaf.Xmpp
open Yaaf.Xmpp.Server
open Yaaf.Xmpp.Runtime
open Yaaf.Xmpp.Runtime.Features
open Yaaf.Xmpp.IM.MessageArchiving
open Yaaf.Xmpp.IM.Server
open Yaaf.Xmpp.IM
open Yaaf.Xmpp.MessageArchiving.Server
open Swensen.Unquote
open Foq
open Yaaf.Xmpp.XmlStanzas
type IIntegrationService =
    abstract ElementReceived : IEvent<StreamElement>
    abstract SendElems : StreamElement list -> unit

[<AutoOpen>]
module IntegrationServiceExtensions = 
    type IIntegrationService with
        member x.SendElem elem = x.SendElems [elem]

type IntegrationHelperPlugin(runtimeConfig : IRuntimeConfig, delivery : ILocalDelivery, registrar : IPluginManagerRegistrar, neg : INegotiationService) =
    let elemReceived = Event<_>()
    let handleElement elem =
        async {
            elemReceived.Trigger elem
            return ()       
        }
    let xmlWatcher =
        { new IXmlPipelinePlugin with
            member x.StreamOpened () = async.Return ()
            member x.ReceivePipeline = 
                { Pipeline.empty "XmlStanzaPlugin XML Pipeline Handler" with
                    HandlerState = 
                        fun info ->
                            HandlerState.ExecuteUnhandled
                    Process = 
                        fun info ->
                            // processing could take a second
                            Async.StartAsTaskImmediate(handleElement info.Result.Element)
                } :> IPipeline<_>
            member x.SendPipeline = Pipeline.emptyPipeline "XmlStanzaPlugin XML Pipeline Handler"
        }
    do 
        registrar.RegisterFor<IXmlPipelinePlugin> xmlWatcher

    interface IIntegrationService with
        member __.ElementReceived = elemReceived.Publish
        member __.SendElems elems = delivery.QueueMessages elems
        
    interface IXmppPlugin with
        member x.PluginService = Service.FromInstance<IIntegrationService,_> x
        member __.Name = "IntegrationHelperPlugin"

module XmppSetup =
    let addIntegrationClientPlugin setup = 
        let setupRuntimeCore (runtime:XmppRuntime) = 
            let mgr = runtime.PluginManager
            mgr.RegisterPlugin<IntegrationHelperPlugin>()
        XmppSetup.AddHelper(setup, ignore, setupRuntimeCore)
    let addIntegrationServerPlugin setup =
        setup
        |> XmppServerSetup.addToAllStreams XmppSetup.addXmppServerPlugin

type IntegrationTestServerSetup =
    {
        CreateServerSetup : ServerSetup
        ServerClientPort : int option
        ServerServerPort : int option
        ServerComponentPort : int option
    }

type IntegrationTestSetup =
    {
        Servers : IntegrationTestServerSetup list
        // lazy to connect after servers are started!
        Clients : (unit -> ClientSetup) list
    }

type IntegrationTestsProvider(config : IntegrationTestSetup) = 
    let setupServer (conf:IntegrationTestServerSetup) = 
        let server = XmppServer(XmppSetup.addIntegrationServerPlugin conf.CreateServerSetup)
        match conf.ServerClientPort with
        | Some clientPort ->
            server.StartListen(StreamType.ClientStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, clientPort)))
            |> ignore
        | None -> ()
        match conf.ServerServerPort with
        | Some clientPort ->
            server.StartListen(StreamType.ServerStream, (System.Net.IPEndPoint(System.Net.IPAddress.Any, clientPort)))
            |> ignore
        | None -> ()
        match conf.ServerComponentPort with
        | Some clientPort ->
            server.StartListen(StreamType.ComponentStream true, (System.Net.IPEndPoint(System.Net.IPAddress.Any, clientPort)))
            |> ignore
        | None -> ()
        server
    let servers = config.Servers |> List.map setupServer
    
    let setupClient (conf: ClientSetup) = 
        
        XmppClient.RawConnect(XmppSetup.addIntegrationClientPlugin conf) :> IXmppClient
    let clients = config.Clients |> List.map (fun f -> f()) |> List.map setupClient
    member x.WaitNegotiation() =
        for client in clients do
            // wait for negotation...
            client.NegotiationTask |> waitTask
    member x.FindClient jid = 
        clients |> Seq.find (fun c -> 
            c.LocalJid.IsSpecialOf jid)
    member x.GetClients jid = 
        clients |> Seq.filter (fun c -> c.LocalJid.IsSpecialOf jid)

/// Provides some integration tests to check if everything works together.
/// These are some basic checks like subscription and sending messages works. 
[<AbstractClass>]
type IntegrationTestsBase (config:IntegrationTestSetup) =
    inherit MyTestClass()
    let mutable provider = Unchecked.defaultof<_>
    
    abstract RemoveSideEffects : unit -> unit
    override x.RemoveSideEffects () = ()

    override x.Setup () =
        base.Setup()
        x.RemoveSideEffects()
        provider <- IntegrationTestsProvider(config)

    override x.TearDown() =
        provider <- Unchecked.defaultof<_>
        base.TearDown()
    member x.GetClient jid = 
        provider.FindClient jid
    member x.WaitNegotiation () = provider.WaitNegotiation()

    member x.SendReceiveMessage (fromClient:IXmppClient, toClient:IXmppClient, message, filter) = 

        let toService = toClient.GetService<IIntegrationService>()
        let fromService = fromClient.GetService<IIntegrationService>()
        toService.ElementReceived 
        |> Event.filter (fun elem -> filter elem)
        |> Event.guard (fun () -> 
            // send message
            fromService.SendElem message
        )
        |> Async.AwaitEvent
        |> Async.StartAsTask
        |> waitTaskI "ReceiveMessageTask"
    member x.FilterMessages (client:IXmppClient) elem =
        let parsed = Parsing.parseStanzaElement client.StreamType.StreamNamespace elem
        Parsing.isContentMessage parsed

    member x.GoOnline(client:IXmppClient) = 
        let imService = client.GetService<IImService>()
        imService.SendPresence(None, PresenceProcessingType.StatusInfo (PresenceStatus.SetStatus PresenceData.Empty))

    member x.GetRoster (client :IXmppClient) = 
        let imService = client.GetService<IImService>()
        imService.RequestRoster(None) |> waitTask

module IntegrationSetupHelper =
    open System.Net
    open System.Net.Sockets
    let nextFreeTcpPort() = 
        let l = new TcpListener(IPAddress.Loopback, 0)
        l.Start()
        let port = (l.LocalEndpoint :?> IPEndPoint).Port
        l.Stop()
        port

    let defaultResourceManager =
        { new Features.IResourceManager with
            member x.IsConnected jid = async.Return false
            member x.Disconnect jid = async.Return()
            member x.GenerateResourceId jid = { jid with Resource = Some(System.Guid.NewGuid().ToString()) } }

    let coreServerConfig domain components authBackend = 
        let cert = Some Test.Yaaf.Xmpp.DevelopmentCertificate.serverCertificate
        XmppServerSetup.CreateDefault domain
        |> XmppServerSetup.addServerCore components cert defaultResourceManager [new Yaaf.Sasl.Plain.PlainServer(authBackend)]
        |> XmppServerSetup.addXmppServerPlugin
        |> XmppServerSetup.addPerUserService
        |> XmppServerSetup.addDiscoPlugin
        
    let imServerConfig rosterStore domain components authBackend  =
        coreServerConfig domain components authBackend 
        |> XmppServerSetup.addIMPlugin { ImServerConfig.Default with RosterStore = rosterStore }
    
    let messagePipelineServerConfig mplugcfg rosterStore domain components authBackend  =
        imServerConfig rosterStore domain components authBackend 
        |> XmppServerSetup.addMessagePipeline
        |> XmppServerSetup.addMessagePlugin mplugcfg
    
    let archivingServerConfig msgStore rosterStore domain components authBackend =
        imServerConfig rosterStore domain components authBackend 
        |> XmppServerSetup.addMessageArchivingPlugin { MessageArchivingServerPluginConfig.Default with MessageArchiveStore = msgStore }
  
    let archivingMessagePipelineServerConfig msgStore mplugcfg rosterStore domain components authBackend =
        messagePipelineServerConfig mplugcfg rosterStore domain components authBackend
        |> XmppServerSetup.addMessageArchivingPlugin { MessageArchivingServerPluginConfig.Default with MessageArchiveStore = msgStore }
  

    let defaultClient (server:IntegrationTestServerSetup) (jid:JabberId) (user,pass)=  
        let config = 
            Yaaf.Xmpp.XmppSetup.CreateSetup()
            |> XmppSetup.AddMessagingClient
        
        let hostName = jid.Domain.FullId
        let hostname, client, stream = 
            Resolve.tryConnectEP (hostName, IPEndPoint(IPAddress.Loopback, server.ServerClientPort.Value))
                |> Async.RunSynchronously
                |> Option.get
        let config = 
            XmppSetup.AddConnectInfo
                (config, 
                 { ConnectInfo.LocalJid = jid
                   ConnectInfo.Login = [ new Yaaf.Sasl.Plain.PlainClient(jid.Localpart.Value, user, pass) ] },
                 { RemoteHostname = hostname
                   Stream = new IOStreamManager(stream :> System.IO.Stream)
                   RemoteJid = Some jid.Domain
                   IsInitializing = true })
        config
