Yaaf.Xmpp.Server
===================
## [Documentation](https://matthid.github.io/Yaaf.Xmpp.Server/)

[![Join the chat at https://gitter.im/matthid/Yaaf.Xmpp.Runtime](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/matthid/Yaaf.Xmpp.Runtime?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

## Build status

**Development Branch**

[![Build Status](https://travis-ci.org/matthid/Yaaf.Xmpp.Server.svg?branch=develop)](https://travis-ci.org/matthid/Yaaf.Xmpp.Server)
[![Build status](https://ci.appveyor.com/api/projects/status/wek4hrna9dm1a09i/branch/develop?svg=true)](https://ci.appveyor.com/project/matthid/yaaf-xmpp-server/branch/develop)

**Master Branch**

[![Build Status](https://travis-ci.org/matthid/Yaaf.Xmpp.Server.svg?branch=master)](https://travis-ci.org/matthid/Yaaf.Xmpp.Server)
[![Build status](https://ci.appveyor.com/api/projects/status/wek4hrna9dm1a09i/branch/master?svg=true)](https://ci.appveyor.com/project/matthid/yaaf-xmpp-server/branch/master)

## Quickstart

This is a xmpp server implementation using the `Yaaf.Xmpp.*` packages.
The implementation is here to provide additional (integration-)tests for the `Yaaf.Xmpp.*` packages and so you can look into an example usage of those packages.

On the other hand it is a full fledged xmpp server ready to use.
Note that this project is at a very early state and some security features are still missing!

If you want to get running:

```bash
git clone https://github.com/matthid/Yaaf.Xmpp.Server.git
cd Yaaf.Xmpp.Server
# or download a .zip or .tar.gz from https://github.com/matthid/Yaaf.Xmpp.Server/tags
build.cmd # or ./build.sh on linux
```

If everything worked you now have a working xmpp server binary, now you need to configure your server:
Just create a file `temp/localServer.yaml`

```yaml

# Your domain
Domain: xmpp.domain.tld

# This are the default ports
ServerPorts:
  - Type: s2s
    Port: 5269
  - Type: c2s
    Port: 5222
  - Type: component
    Port: 5347

# You need to create the certificates four your domain and insert the correct paths here.
Certificate:
  Private: "C:/projects/Yaaf.Xmpp.Server/temp/key.pem"
  Public: "C:/projects/Yaaf.Xmpp.Server/temp/cert.pem"
  Password : ""

# Your components
Components: []
# If you want to specify components they look like this:
#Components:
#  - Domain: gtalk.devel-xmpp.yaaf.de
#    Secret: secret_gtalk
#  - Domain: facebook.devel-xmpp.yaaf.de
#    Secret: secret_facebook

# currently only ldap is supported
Authentication:
  - Type: ldap
    ConnectionString: "Server=ldap.domain.tld;Port=636;SSL=true"
    MapUserId: "uid={0},ou=People,dc=domain,dc=tld"

# You can specify mysql or mssql
RosterStore:
  - Type: mysql
    ConnectionString: Server=localhost;Database=xmpp;Uid=xmpp;Pwd=password
# a mssql connection string looks like this (for database files): Data Source=(LocalDb)\v11.0;AttachDbFilename=|DataDirectory|\rosterdb-nunit.mdf;Integrated Security=True
# or: Data Source=(local)\\SQL2014;Database=rosterstore_nunit;User ID=sa;Password=Password12!

MessageArchive:
# enable this if you want that your messages get written to an imap folder
# note that the given user needs access to the mailbox specified in `Folder`
#  - Type: "imap"
#    Writeonly: true
#    ReplacePreferenceStoreWithMemory: false
#    ConnectionString: "Server=imap.domain.tld;Uid=user;Pwd=password;Folder=user.{0}.Chats.LocalDevelXmpp;Timeout=5000"
  - Type: "mysql"
    Writeonly: false
    ReplacePreferenceStoreWithMemory: true
    ConnectionString: "Server=localhost;Database=xmpp;Uid=xmpp;Pwd=password"

```

If you use a real database (ie no database files) you need to create the specified user and grant access. For example

```mysql
GRANT ALL PRIVILEGES ON xmpp.* To 'xmpp'@'localhost' IDENTIFIED BY 'password';
```

Now you can run the server:

```bash
runServer.cmd # or ./runServer.sh on linux
```

