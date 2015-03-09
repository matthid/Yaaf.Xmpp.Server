// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.Xmpp.Server
open Yaaf.Logging
type ConfigFile = XmppServerConfig.ConfigFile
type Args =
  { IsDaemon : bool
    ConfigFile : ConfigFile }
    

module CommandLine =
    let help = """Yaaf.Xmpp.Server, Version: {0}
Usage: Yaaf.Xmpp.Server.exe [options]
Description:

Starts a new xmpp server instance.

Options:

--help               : Print this help
--daemon             : Run in daemon mode
--configFile <file>  : Use the specified config file instead of 'config.yaml'

    """
    let parseArgs (args:string[]) =
        if args |> Seq.exists (fun i -> i = "--help") || args |> Seq.exists (fun i -> i = "-h") then
          None
        else
        let isDaemon = args |> Seq.exists (fun i -> i = "--daemon")
        let getNamed args name =
          let rec getNamedHelper current args name =
            let remainder = args |> Seq.skipWhile (fun i -> i <> name)
            if Seq.isEmpty remainder then
               current
            else
                let next = remainder |> Seq.skip 1
                if Seq.isEmpty next then
                    current
                else
                    getNamedHelper ((Seq.head next) :: current) (next |> Seq.skip 1) name
          getNamedHelper [] args name
        let getNamedSingle args name = 
            match getNamed args name with
            | h :: _ -> Some h
            | _ -> None

        let configFile =
            match getNamedSingle args "--configFile" with
            | Some configFile -> configFile
            | _ -> "config.yml"
        let config = ConfigFile()
        config.Load configFile
        Some { IsDaemon= isDaemon; ConfigFile = config }
        
    let mutable exited = false
    let mutable cleanedUp = false
    let mutable cleanLock = obj()
    let mutable xmppServer = Unchecked.defaultof<_>

    let runServer (args:Args) =
        let runServer () =
            xmppServer <- XmppServerConfig.StartFromConfig args.ConfigFile

        let cleanUp () = 
            let doClean =
                lock cleanLock (fun () ->
                    if not cleanedUp then
                        cleanedUp <- true
                        true
                    else false)
            if doClean then
                printfn "Shuting down server..."
                Log.Info (fun () -> "Shuting down server...")
                exited <- true
                let exitTask = Async.StartAsTask(xmppServer.Shutdown(true))
                let mutable waitTime = 5000
                let waitUnit = 100
                while waitTime > 0 && not exitTask.IsCompleted do
                    System.Threading.Thread.Sleep waitUnit
                    waitTime <- waitTime - waitUnit
                if not exitTask.IsCompleted then
                    printfn "Server doesn't want to exit, we will kill ourselfs..."
                    Log.Crit (fun () -> "Server doesn't want to exit, we will kill ourselfs...")
                    System.Environment.Exit 1337
                elif exitTask.IsFaulted then
                    let errMsg = sprintf "Server doesn't experienced exception on exit: %A" exitTask.Exception
                    printfn "%s" errMsg
                    Log.Err (fun () -> errMsg)
                else 
                    printfn "Server shut down successfully..."
                    Log.Info (fun () -> "Server shut down successfully...")
                //(msgStore:>System.IDisposable).Dispose()
        let isMono = try System.Type.GetType("Mono.Runtime") <> null with e -> false 
        if isMono then
            let signals = [|
                new Mono.Unix.UnixSignal (Mono.Unix.Native.Signum.SIGTERM)
                //new UnixSignal (Mono.Unix.Native.Signum.SIGUSR1),
            |]
         
            let handler (signal:Mono.Unix.UnixSignal) =
                exited <- true
                cleanUp()

            let signalThread = new System.Threading.Thread(fun () ->
                while true do
                    // Wait for a signal to be delivered
                    let index = Mono.Unix.UnixSignal.WaitAny (signals, -1)
                    System.Threading.Tasks.Task.Run(fun () -> handler (signals.[index])) |> ignore)
            signalThread.IsBackground <- true
            signalThread.Start()

        let isDaemon = args.IsDaemon
        let consoleLogging = if isDaemon then LoggingHelper.level_off else LoggingHelper.level_verb
        use runActivity = LoggingHelper.startLogging (LoggingHelper.newName()) consoleLogging LoggingHelper.level_all

        let greet = sprintf "Starting server with (%A)" args
        Log.Info (fun () -> greet)
        runServer ()
        if isDaemon then
            printfn "Running in daemon mode, ignoring any input."
            Log.Info (fun () -> "Running in daemon mode, ignoring any input.")
            while not exited do
                System.Threading.Thread.Sleep(1000)
        else
            try
                printfn "%s" greet
                printfn "Write \"exit\" and press return to exit the server..."
        
                while not exited do
                    let maybeLine = Reader.TryReadLine 1000
                    match maybeLine with
                    | Some line ->
                        if (line = "exit") then
                            exited <- true
                        else
                            printfn "\"%s\" command was not understood, write \"exit\" to exit the server" line
                    | None -> ()
            with exn ->
                printfn "Could not read from console, running until we get killed..."
                while not exited do
                    System.Threading.Thread.Sleep(1000)

        cleanUp()

        printfn "All shut down, exiting..."
        0


    [<EntryPoint>]
    let main (args:string array) =
      match parseArgs args with
      | None -> 
        System.Console.WriteLine (help, System.AssemblyVersionInformation.Version)
        0
      | Some args ->
        runServer args