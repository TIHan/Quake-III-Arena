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

/// ErlCommand
type ErlCommand =
    | Ping

    member this.Id with get () =
        let type' = this.GetType ()
        let property = type'.GetProperty "Tag"

        property.GetValue (this) :?> int

/// ErlEvent
type ErlEvent =
    | Pong
    | CommandSent of string

    member this.Id with get () =
        let type' = this.GetType ()
        let property = type'.GetProperty "Tag"

        property.GetValue (this) :?> int

module ErlNet =
    let mutable private socket_ : Socket option = None
    let mutable private buffer_ = Array.zeroCreate<byte> 8192

    let init () =
        let socket = Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, Blocking = false)

        socket.Connect ("localhost", 37950)
        socket_ <- Some <| socket

    let checkIsConnected () = socket_.IsSome

    let dispatch (cmd: ErlCommand) =
        match socket_ with
        | None -> ()
        | Some socket ->

        match cmd with
        | Ping ->
            socket.Send([| byte cmd.Id |]) |> ignore
        | _ ->
            ()    

    let handleEvents (f: ErlEvent -> unit) =
        match socket_ with
        | None -> ()
        | Some socket ->

        while socket.Available <> 0 do
            let size = socket.Receive(buffer_)
            match System.Text.Encoding.UTF8.GetString(buffer_.[..size - 1]) with
            | "Pong" -> f Pong
            | x -> f <| CommandSent x

