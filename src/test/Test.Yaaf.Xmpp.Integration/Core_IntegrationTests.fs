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
open Yaaf.Sasl.Plain
open Yaaf.Xmpp
open Yaaf.Xmpp.Server
open Yaaf.Xmpp.Runtime
open Yaaf.Xmpp.Runtime.Features
open Yaaf.Xmpp.MessageArchiving
open Yaaf.Xmpp.IM.Server
open Yaaf.Xmpp.IM
open Swensen.Unquote
open Foq
open Yaaf.Xmpp.XmlStanzas
// Tests for the core server configuration (even without IM plugin)
[<AbstractClass>]
type Core_IntegrationTests_Base (parameters) =
    inherit IntegrationTestsBase(
        let serverConfigCreator, authBackend, getUserPassForUser = parameters
        let yaafServer = 
            {
                CreateServerSetup = serverConfigCreator "yaaf.de" ([]:ComponentConfig list) authBackend
                ServerClientPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
                ServerServerPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
                ServerComponentPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
            }
        let testClient () = 
            IntegrationSetupHelper.defaultClient yaafServer (JabberId.Parse "test@yaaf.de") (getUserPassForUser "test")
        let otherClient() = 
            IntegrationSetupHelper.defaultClient yaafServer (JabberId.Parse "other@yaaf.de") (getUserPassForUser "other")
        {
            Servers = [yaafServer]
            Clients = [testClient; otherClient]
        })
    
    member x.CheckSendSimpleMessage () = 
        x.WaitNegotiation()
        let fromJid, toJid = JabberId.Parse "test@yaaf.de", JabberId.Parse "other@yaaf.de"
        let fromClient, toClient = x.GetClient(fromJid), x.GetClient(toJid)

        let rawmessage = 
            sprintf 
                "<message xmlns='%s' from='%s' to='%s'><body>test</body></message>" 
                KnownStreamNamespaces.clientNS (fromJid.FullId) (toJid.FullId)
        let message = 
            System.Xml.Linq.XElement.Parse (rawmessage)

        let rawexpected = 
            sprintf 
                "<message xmlns='%s' from='%s' to='%s'><body>test</body></message>" 
                KnownStreamNamespaces.clientNS (fromClient.LocalJid.FullId) (toJid.FullId)
        let expected = System.Xml.Linq.XElement.Parse rawexpected
          
        let received =
            x.SendReceiveMessage(fromClient, toClient, message, x.FilterMessages toClient)

        test <@ Yaaf.Xml.Core.equalXNode received expected @>

    member x.OtherGoOnline() =
        x.WaitNegotiation()
        let toJid = JabberId.Parse "other@yaaf.de"
        let toClient = x.GetClient(toJid)
        x.GoOnline toClient

[<TestFixture>]
[<Category("Integration")>]
type Memory_Core_IntegrationTests () =
    inherit Core_IntegrationTests_Base(
        let user = "username"
        let pass = "password"
        let userPass = (user, pass)
        let source = (DefaultUserSources.SingleUserSource true user pass)
        IntegrationSetupHelper.coreServerConfig, source, (fun _ -> userPass))
        
    [<Test>]
    member x.``Check that sending a message works in Core`` () = 
        x.CheckSendSimpleMessage()
 

[<TestFixture>]
[<Category("Integration")>]       
type Memory_CoreOnIm_IntegrationTests () =
    inherit Core_IntegrationTests_Base(
        let user = "username"
        let pass = "password"
        let userPass = (user, pass)
        let source = (DefaultUserSources.SingleUserSource true user pass)
        IntegrationSetupHelper.imServerConfig (new MemoryRosterStore()), source, (fun _ -> userPass))
        
    [<Test>]
    member x.``Check that sending a message works with IM plugin`` () = 
        x.CheckSendSimpleMessage()

        
[<TestFixture>]
[<Category("Integration")>]       
type Memory_CoreOnMessagePipeline_IntegrationTests () =
    inherit Core_IntegrationTests_Base(
        let user = "username"
        let pass = "password"
        let userPass = (user, pass)
        let source = (DefaultUserSources.SingleUserSource true user pass)
        let userConfig = 
            { new  IUserMessageUserConfig with
                member x.GetConfig (toJid, fromJid) = 
                    Task.FromResult
                            {
                                // xmpp.core compliant
                                ResourceSendingStrategy = ResourceSendingStrategy.NonNegativeResources
                                OfflineMessagesEnabled = true
                            }
            }
        let mplugcfg = 
            { ImServerMessagePluginConfig.Default with UserConfig = userConfig; OfflineStorage = None}
        IntegrationSetupHelper.messagePipelineServerConfig mplugcfg (new MemoryRosterStore()), source, (fun _ -> userPass))
        
    [<Test>]
    member x.``Check that sending a message works with message Pipeline`` () = 
        // Xmpp.IM required to go online first
        x.OtherGoOnline()
        x.CheckSendSimpleMessage()
        

[<TestFixture>]
[<Category("Integration")>]      
type Memory_CoreOnArchiving_IntegrationTests () =
    inherit Core_IntegrationTests_Base(
        let user = "username"
        let pass = "password"
        let userPass = (user, pass)
        let source = (DefaultUserSources.SingleUserSource true user pass)
        IntegrationSetupHelper.archivingServerConfig (new MemoryMessageArchivingStore()) (new MemoryRosterStore()), source, (fun _ -> userPass))

    [<Test>]
    member x.``Check that sending a message works with archiving`` () = 
        // because we don't use the messaging plugin we have default behavior
        x.CheckSendSimpleMessage()


[<TestFixture>]
[<Category("Integration")>]       
type Memory_CoreOnArchivingMessagePipeline_IntegrationTests () =
    inherit Core_IntegrationTests_Base(
        let user = "username"
        let pass = "password"
        let userPass = (user, pass)
        let source = (DefaultUserSources.SingleUserSource true user pass)
        let userConfig = 
            { new  IUserMessageUserConfig with
                member x.GetConfig (toJid, fromJid) = 
                    Task.FromResult
                            {
                                // xmpp.core compliant
                                ResourceSendingStrategy = ResourceSendingStrategy.NonNegativeResources
                                OfflineMessagesEnabled = true
                            }
            }
        let mplugcfg = 
            { ImServerMessagePluginConfig.Default with UserConfig = userConfig; OfflineStorage = None}
        
        IntegrationSetupHelper.archivingMessagePipelineServerConfig (new MemoryMessageArchivingStore()) mplugcfg (new MemoryRosterStore()), source, (fun _ -> userPass))
        
    [<Test>]
    member x.``Check that sending a message works with message pipleine and archiving`` () = 
        // Xmpp.IM required to go online first
        x.OtherGoOnline()
        x.CheckSendSimpleMessage()