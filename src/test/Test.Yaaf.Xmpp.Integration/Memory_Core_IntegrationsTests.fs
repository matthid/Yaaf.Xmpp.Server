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
open Swensen.Unquote
open Foq
open Yaaf.Xmpp.XmlStanzas


[<TestFixture>]
[<Category("Integration")>]
type SimpleMemoryIntegrationTests ()=
    inherit IntegrationTestsBase(
        let userPass = ("username", "password")
        let yaafServer = 
            {
                CreateServerSetup = IntegrationSetupHelper.memoryServerConfig "yaaf.de" [] userPass
                ServerClientPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
                ServerServerPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
                ServerComponentPort = Some (IntegrationSetupHelper.nextFreeTcpPort())
            }
        let testClient () = 
            IntegrationSetupHelper.defaultClient yaafServer (JabberId.Parse "test@yaaf.de") userPass
        let otherClient() = 
            IntegrationSetupHelper.defaultClient yaafServer (JabberId.Parse "other@yaaf.de") userPass
        {
            Servers = [yaafServer]
            Clients = [testClient; otherClient]
        })
    
    [<Test>]
    member x.``Check that sending a message works`` () = 
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
            x.SendReceiveMessage(fromClient, toClient, message)

        test <@ Yaaf.Xml.Core.equalXNode received expected @>
    