﻿(*
Copyright (C) 2013 William F. Smith

This program is free software; you can redistribute it
and/or modify it under the terms of the GNU General Public License as
published by the Free Software Foundation; either version 2 of the License,
or (at your option) any later version.

This program is distributed in the hope that it will be
useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

Derivative of Quake III Arena source:
Copyright (C) 1999-2005 Id Software, Inc.
*)

namespace Engine.Net

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Runtime.InteropServices
open System.Threading
open System.Diagnostics
open Microsoft.FSharp.NativeInterop
open FSharpx.Collections
open Engine.NativeInterop

/// ErlRequest
type ErlRequest =
    | Ping

/// ErlResponse
type ErlResponse =
    | Pong

module ErlNet =
    let private callLock_ = obj ()
    let mutable private callSocket_ : Socket option = None
    let mutable private callBuffer_ = Array.zeroCreate<byte> 8192

    let tryInit () =
        try
            let callSocket = Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, NoDelay = true)

            callSocket.Connect ("localhost", 37950)
            callSocket_ <- Some <| callSocket
            true
        with
        _ -> false

    let call (req: ErlRequest) =
        lock callLock_ (fun () ->

            match callSocket_ with
            | None -> None
            | Some socket ->

            match req with
            | Ping ->
                let msg = [| 0uy |]

                socket.Send msg |> ignore

                let size = socket.Receive(callBuffer_)
                let res = callBuffer_.[..size - 1]

                match res with
                | [| 0uy |] -> Some Pong
                | _ -> None
            | _ ->
                raise <| Exception "Bad request."

        )

