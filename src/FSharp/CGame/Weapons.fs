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

namespace CGame

// Disable native interop warnings
#nowarn "9"
#nowarn "51"

open Engine.Math
open Engine.Renderer
open System.Diagnostics.Contracts

// Types
// TODO: Move this to a Types.fs

module Constants =
    [<Literal>]
    let LandDeflectTime = 150

    [<Literal>]
    let LandReturnTime = 300

/// <summary>
/// Based on Q3: cg_t
/// CGame
///
/// types are currently unordered, sort of
/// TODO: No-where near finished with this record.
/// </summary>
type CGame =
    {
        Time: int;              // this is the time value that the client
                                // is rendering at.

        LandChange: single;     // for landing hard
        LandTime: int;

        RefDef: RefDef;
        RefDefViewAngles: Vector3;

        // temp working variables for player view
        BobCycle: int;
        BobFractionSin: single;
        XYSpeed: single;
    }

// End Types

module Weapons =
(*
static void CG_CalculateWeaponPosition( vec3_t origin, vec3_t angles ) {
	float	scale;
	int		delta;
	float	fracsin;

	VectorCopy( cg.refdef.vieworg, origin );
	VectorCopy( cg.refdefViewAngles, angles );

	// on odd legs, invert some angles
	if ( cg.bobcycle & 1 ) {
		scale = -cg.xyspeed;
	} else {
		scale = cg.xyspeed;
	}

	// gun angles from bobbing
	angles[ROLL] += scale * cg.bobfracsin * 0.005;
	angles[YAW] += scale * cg.bobfracsin * 0.01;
	angles[PITCH] += cg.xyspeed * cg.bobfracsin * 0.005;

	// drop the weapon when landing
	delta = cg.time - cg.landTime;
	if ( delta < LAND_DEFLECT_TIME ) {
		origin[2] += cg.landChange*0.25 * delta / LAND_DEFLECT_TIME;
	} else if ( delta < LAND_DEFLECT_TIME + LAND_RETURN_TIME ) {
		origin[2] += cg.landChange*0.25 * 
			(LAND_DEFLECT_TIME + LAND_RETURN_TIME - delta) / LAND_RETURN_TIME;
	}

#if 0
	// drop the weapon when stair climbing
	delta = cg.time - cg.stepTime;
	if ( delta < STEP_TIME/2 ) {
		origin[2] -= cg.stepChange*0.25 * delta / (STEP_TIME/2);
	} else if ( delta < STEP_TIME ) {
		origin[2] -= cg.stepChange*0.25 * (STEP_TIME - delta) / (STEP_TIME/2);
	}
#endif

	// idle drift
	scale = cg.xyspeed + 40;
	fracsin = sin( cg.time * 0.001 );
	angles[ROLL] += scale * fracsin * 0.01;
	angles[YAW] += scale * fracsin * 0.01;
	angles[PITCH] += scale * fracsin * 0.01;
}
*)
    /// <summary>
    /// Based on Q3: CG_CalculateWeaponPosition
    /// CalculateWeaponPosition
    /// </summary>
    [<Pure>]
    let calculateWeaponPosition (cg: CGame) =
        let origin = cg.RefDef.ViewOrigin
        let angles = cg.RefDefViewAngles

        // on odd legs, invert some angles
        let scale =
            match cg.BobCycle &&& 1 with
            | 0 -> -cg.XYSpeed
            | _ -> cg.XYSpeed

        // gun angles from bobbing
        let angles =
            Vector3 (
                angles.X + (cg.XYSpeed * cg.BobFractionSin * 0.005f), // PITCH
                angles.Y + (scale * cg.BobFractionSin * 0.01f), // YAW
                angles.Z + (scale * cg.BobFractionSin * 0.005f) // ROLL
            )


        let deltaTime = cg.Time - cg.LandTime
        
        let originZ =
            // drop the weapon when landing
            match deltaTime with
            | x when x < Constants.LandDeflectTime ->
                origin.Z + (cg.LandChange * 0.25f * (single x / single Constants.LandDeflectTime))
            | x when x < Constants.LandDeflectTime + Constants.LandReturnTime ->
                origin.Z + (cg.LandChange * 0.25f * single (Constants.LandDeflectTime + Constants.LandReturnTime - x) / single Constants.LandReturnTime)
            | _ -> origin.Z

        // idle drift
        let scale = cg.XYSpeed + 40.f
        let fractionSin = sin <| single cg.Time * 0.001f
        let angles =
            Vector3 (
                angles.X + (scale * fractionSin * 0.01f), // PITCH
                angles.Y + (scale * fractionSin * 0.01f), // YAW
                angles.Z + (scale * fractionSin * 0.01f) // ROLL
            )
        let origin =
            Vector3 (
                origin.X,
                origin.Y,
                originZ
            )
        (origin, angles)

