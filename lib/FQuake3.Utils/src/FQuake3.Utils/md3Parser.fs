﻿module FQuake3.Utils.Md3

open FSharp.LiteParsec
open FQuake3.Md3
open FQuake3.Utils.Internal.Md3Parser

let parse bytes = run pmd3 bytes