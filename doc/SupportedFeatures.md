# F# Yaaf.Xmpp.Server Supported Specifications

## Supported RFCs

- [RFC 6120 - XMPP Core](http://xmpp.org/rfcs/rfc6120.html) via https://github.com/matthid/Yaaf.Xmpp.Runtime
- [RFC 6121 - XMPP IM](http://xmpp.org/rfcs/rfc6121.html) via https://github.com/matthid/Yaaf.Xmpp.IM

## RFCs (planned)

- [RFC 6122 - XMPP AF (planned)](http://xmpp.org/rfcs/rfc6122.html)

## Supported XEPs

- [XEP-0030: Service Discovery](http://xmpp.org/extensions/xep-0030.html) via https://github.com/matthid/Yaaf.Xmpp.ServiceDiscovery
- [XEP-0136: Message Archiving](http://xmpp.org/extensions/xep-0136.html) (XEP-0059 is missing) via https://github.com/matthid/Yaaf.Xmpp.MessageArchiving
- [XEP-0054: vcard-temp](http://xmpp.org/extensions/xep-0054.html) via https://github.com/matthid/Yaaf.Xmpp.VCARd

- [XEP-0203: Delayed Delivery](http://xmpp.org/extensions/xep-0203.html) via https://github.com/matthid/Yaaf.Xmpp.IM/blob/develop/src/source/Yaaf.Xmpp.IM/DelayedDelivery.fs
- [XEP-0280: Message Carbons](http://xmpp.org/extensions/xep-0280.html) via https://github.com/matthid/Yaaf.Xmpp.IM/blob/develop/src/source/Yaaf.Xmpp.IM/MessageCarbons.fs
- [XEP-0297: Stanza Forwarding](http://xmpp.org/extensions/xep-0297.html) via https://github.com/matthid/Yaaf.Xmpp.IM/blob/develop/src/source/Yaaf.Xmpp.IM/StanzaForwarding.fs

### Server Only XEPs

- [XEP-0114: Jabber Component Protocol](http://xmpp.org/extensions/xep-0114.html) via https://github.com/matthid/Yaaf.Xmpp.Runtime/blob/develop/src/source/Yaaf.Xmpp.Runtime/Runtime/ComponentNegotiation.fs
- [XEP-0225: Component Connections](http://xmpp.org/extensions/xep-0225.html) (not really implemented but it is trivial to support)
- [XEP-0160: Best Practices for Handling Offline Messages](http://xmpp.org/extensions/xep-0160.html) via https://github.com/matthid/Yaaf.Xmpp.MessageArchiving

## XEPs (started or soon to be started, in this order)

- [XEP-0079: Advanced Message Processing](http://xmpp.org/extensions/xep-0079.html)

- SERVER: [XEP-0220: Server Dialback](http://xmpp.org/extensions/xep-0220.html)
- SERVER: [XEP-0288: Bidirectional Server-to-Server Connections](http://xmpp.org/extensions/xep-0288.html)

- [XEP-0059: Result Set Management](http://xmpp.org/extensions/xep-0059.html)

## XEPs (planned)

Note that the "CLIENT:" XEPs are client only XEPs and therefore already supported by the server.

- [XEP-0199: XMPP Ping](http://xmpp.org/extensions/xep-0199.html)
- [XEP-0144: Roster Item Exchange](http://xmpp.org/extensions/xep-0144.html)
- [XEP-0082: XMPP Date and Time Profiles](http://xmpp.org/extensions/xep-0082.html)
- [XEP-0045: Multi-User Chat](http://xmpp.org/extensions/xep-0045.html)
- [XEP-0016: Privacy Lists](http://xmpp.org/extensions/xep-0016.html)
- [XEP-0126: Invisibility](http://xmpp.org/extensions/xep-0126.html), we extend it in a way so that no "offline -> change -> online" cycle is necessary.

- CLIENT: [XEP-0100: Gateway Interaction](http://xmpp.org/extensions/xep-0100.html)
- CLIENT: [XEP-0115: Entity Capabilities](http://xmpp.org/extensions/xep-0115.html)
- CLIENT: [XEP-0071: XHTML-IM](http://xmpp.org/extensions/xep-0071.html)
- CLIENT: [XEP-0085: Chat State Notifications](http://xmpp.org/extensions/xep-0085.html)
- CLIENT: [XEP-0065: SOCKS5 Bytestreams](http://xmpp.org/extensions/xep-0065.html)


## More XEPs

Note that all client only XEPs are always supported by the server, even if they are not supported by the client implemented in this repository.

