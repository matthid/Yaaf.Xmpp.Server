#!/bin/bash
if test "$OS" = "Windows_NT"
then
  # use .Net
  MONO=""
  DEFINE="WIN64"
  FSI="C:\Program Files (x86)\Microsoft SDKs\F#\3.1\Framework\v4.0\fsi.exe"
else
  # use mono
  command -v mono >/dev/null 2>&1 || { echo >&2 "Please install mono dependency."; exit 1; }
  myMono="mono --debug --runtime=v4.0"
  FSI="fsharpi"

  MONO="$myMono"
  DEFINE="MONO"
fi

$MONO src/source/Yaaf.Xmpp.Server/bin/Debug/Yaaf.Xmpp.Server.exe --configFile temp/localServer.yaml $@