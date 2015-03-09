// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
module Yaaf.Xmpp.Server.Reader

let getInput = new System.Threading.AutoResetEvent(false)
let gotInput = new System.Threading.AutoResetEvent(false)

let reader, readLine  =
    let input = ref null
    (fun () ->
        while true do
            getInput.WaitOne() |> ignore
            input := System.Console.ReadLine();
            gotInput.Set() |> ignore),
    (fun (timeOutMillisecs:int) ->
        getInput.Set() |> ignore
        let success = gotInput.WaitOne(timeOutMillisecs)
        if success then
            Some !input
        else
            None)

let inputThread = new System.Threading.Thread(reader)
do
    inputThread.IsBackground <- true
    inputThread.Start()
let TryReadLine time = readLine time
let ReadLine time = 
    match readLine time with
    | Some s -> s
    | None -> 
        Microsoft.FSharp.Core.Operators.raise <| new System.TimeoutException("User did not provide input within the timelimit.")
