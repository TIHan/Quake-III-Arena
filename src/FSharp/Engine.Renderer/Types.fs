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

// Disable native interop warnings
#nowarn "9"
#nowarn "51"

namespace Engine.Renderer

open System
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Diagnostics
open System.Diagnostics.Contracts
open Microsoft.FSharp.NativeInterop
open FSharpx.Collections
open Engine.Core
open Engine.Math
open Engine.NativeInterop

/// <summary>
/// Based on Q3: CULL_IN, CULL_CLIP, CULL_OUT
/// ClipType
/// </summary>
type ClipType =
    | In = 0    // completely unclipped
    | Clip = 1  // clipped by one or more planes
    | Out = 2   // completely outside the clipping planes

/// <summary>
/// Based on Q3: PLANE_X, PLANE_Y, PLANE_Z, PLANE_NON_AXIAL
/// PlaneType
///
/// plane types are used to speed some tests
/// 0-2 are axial planes
/// </summary>
type PlaneType =
    | X = 0
    | Y = 1
    | Z = 2
    | NonAxial = 3

/// <summary>
/// Axis
/// </summary>
type Axis =
    { X: Vector3; Y: Vector3; Z: Vector3 }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.X
            | 1 -> this.Y
            | 2 -> this.Z
            | _ -> raise <| IndexOutOfRangeException ()

[<CompilationRepresentation (CompilationRepresentationFlags.ModuleSuffix)>]
module Axis =
    let zero = { X = Vector3.zero; Y = Vector3.zero; Z = Vector3.zero }
    let identity = { X = Vector3.unitX; Y = Vector3.unitY; Z = Vector3.unitZ }

/// <summary>
/// Rgba
/// </summary>
type Rgba =
    { R: byte; G: byte; B: byte; A: byte }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.R
            | 1 -> this.G
            | 2 -> this.B
            | 3 -> this.A
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Based on Q3: orientation_t
/// Orientation
/// </summary>
type Orientation =
    { Origin: Vector3; Axis: Axis }

/// <summary>
/// Based on Q3: orientationr_t
/// OrientationR
///
/// Note: Should this be a record type? It is over 64 bytes, don't know for sure.
/// </summary>
type OrientationR =
    {
        /// <summary>
        /// in world coordinates
        /// </summary>
        Origin: Vector3;

        /// <summary>
        /// orientation in world
        /// </summary>
        Axis : Axis;

        /// <summary>
        /// viewParms->or.origin in local coordinates
        /// FIXME: This directly points to viewParms orientation origin? Yuck.
        /// </summary>
        ViewOrigin: Vector3;
        ModelMatrix: Matrix4x4;
    }

/// <summary>
/// Based on Q3: cplane_t
/// Plane
/// </summary>
type Plane =
    {
        Normal: Vector3;
        Distance: single;

        /// <summary>
        /// for fast side tests: 0,1,2 = axial, 3 = nonaxial
        /// </summary>
        Type: PlaneType;

        /// <summary>
        /// signx + (signy<<1) + (signz<<2), used as lookup during collision
        /// </summary>
        SignBits: byte;
    }

    /// <summary>
    /// Based on Q3: SetPlaneSignBits
    /// CalculateSignBits
    /// </summary>
    static member inline CalculateSignBits (normal: Vector3) =
        let rec calculatePlaneSignBits bits acc =
            match acc with
            | 3 -> bits
            | _ ->
                calculatePlaneSignBits (match normal.[acc] < 0.f with | true -> bits ||| (1uy <<< acc) | _ -> bits) (acc + 1)

        calculatePlaneSignBits 0uy 0

[<CompilationRepresentation (CompilationRepresentationFlags.ModuleSuffix)>]
module Plane =
    let zero = { Normal = Vector3.zero; Distance = 0.f; Type = PlaneType.X; SignBits = 0uy }
    let calculateSignBits (plane: Plane) =
        Plane.CalculateSignBits plane.Normal

    /// <summary>
    /// Based on Q3: PlaneFromPoints
    /// InitFromPoints
    ///
    /// The normal will point out of the clock for clockwise ordered points
    /// </summary>
    let inline ofPoints (a: Vector3) (b: Vector3) (c: Vector3) =
        let d1 = b - a
        let d2 = c - a
        let cross = Vector3.crossProduct d2 d1
        let normal = Vector3.normalize cross
        
        match Vector3.length cross with
        | 0.f -> { Normal = normal; Distance = 0.f; Type = PlaneType.X; SignBits = 0uy }
        | _ -> { Normal = normal; Distance = Vector3.dotProduct a normal; Type = PlaneType.X; SignBits = 0uy }

/// <summary>
/// Bounds
/// </summary>
type Bounds =
    { Bounds0: Vector3; Bounds1: Vector3 }   

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.Bounds0
            | 1 -> this.Bounds1
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Frustum
///
/// TODO: Find out if this is truly left, right, bottom, and top in this order
/// </summary>
type Frustum =
    {
        Left: Plane;
        Right: Plane;
        Bottom: Plane;
        Top: Plane;
    }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.Left
            | 1 -> this.Right
            | 2 -> this.Bottom
            | 3 -> this.Top
            | _ -> raise <| IndexOutOfRangeException ()

[<CompilationRepresentation (CompilationRepresentationFlags.ModuleSuffix)>]
module Frustum =
    [<Literal>]
    let size = 4

/// <summary>
/// Based on Q3: viewParms_t
/// ViewParms
/// </summary>
type ViewParms =
    {
        Orientation: OrientationR;
        World: OrientationR;
        PvsOrigin: Vector3;         // may be different than or.origin for portals
        IsPortal: bool;             // true if this view is through a portal
        IsMirror: bool;             // the portal is a mirror, invert the face culling
        FrameSceneId: int;          // copied from tr.frameSceneNum
        FrameCount: int;            // copied from tr.frameCount
        PortalPlane: Plane;         // clip anything behind this if mirroring
        ViewportX: int;
        ViewportY: int;
        ViewportWidth: int;
        ViewportHeight: int;
        FovX: single;
        FovY: single;
        ProjectionMatrix: Matrix4x4;
        Frustum: Frustum;
        VisibilityBounds: Bounds;
        ZFar: single;
    }

/// <summary>
/// Based on Q3: refEntityType_t
/// RefEntityType
/// </summary>
type RefEntityType =
    | Model = 0
    | Poly = 1
    | Sprite = 2
    | Beam = 3
    | RailCore = 4
    | RailRings = 5
    | Lightning = 6
    | PortalSurface = 7     // doesn't draw anything, just info for portals
    | MaxRefEntityType = 8

/// <summary>
/// Based on Q3: refEntity_t
/// RefEntity
/// </summary>
type RefEntity =
    {
        Type: RefEntityType;
        RenderFx: int;
        ModelHandle: int;                   // opaque type outside refresh
        LightingOrigin: Vector3;            // so multi-part models can be lit identically (RF_LIGHTING_ORIGIN)
        ShadowPlane: single;                // projection shadows go here, stencils go slightly lower
        Axis: Axis;                         // rotation vectors
        HasNonNormalizedAxes: bool;         // axis are not normalized, i.e. they have scale
        Origin: Vector3;                    // also used as MODEL_BEAM's "from"
        Frame: int;                         // also used as MODEL_BEAM's diameter
        OldOrigin: Vector3;                 // also used as MODEL_BEAM's "to"
        OldFrame: int;
        BackLerp: single;                   // 0.0 = current, 1.0 = old
        SkinId: int;                        // inline skin index
        CustomSkinHandle: int;              // NULL for default skin
        CustomShaderHandle: int;            // use one image for the entire thing
        ShaderRgba: Rgba;                   // colors used by rgbgen entity shaders
        ShaderTextureCoordinate: Vector2;   // texture coordinates used by tcMod entity modifiers
        ShaderTime: single;                 // subtracted from refdef time to control effect start times
        Radius: single;
        Rotation: single;
    }

/// <summary>
/// Based on Q3: trRefEntity_t
/// TrRefEntity
///
/// a trRefEntity_t has all the information passed in by
/// the client game, as well as some locally derived info
/// </summary>
type TrRefEntity =
    {
        Entity: RefEntity;
        AxisLength: single;         // compensate for non-normalized axis
        NeedDlights: bool;          // true for bmodels that touch a dlight
        IsLightingCalculated: bool;
        LightDirection: Vector3;    // normalized direction towards light
        AmbientLight: Vector3;      // color normalized to 0-255
        AmbientLightInt: int;       // 32 bit rgba packed
        DirectedLight: Vector3;
    }

/// <summary>
/// Based on Q3: dlight_t
/// Dlight
/// </summary>
type Dlight =
    {
        Origin: Vector3;

        /// <summary>
        /// range from 0.0 to 1.0, should be color normalized
        /// </summary>
        Color: Vector3;
        Radius: single;

        /// <summary>
        /// origin in local coordinate system
        /// </summary>
        Transformed: Vector3;

        /// <summary>
        /// texture detail is lost tho when the lightmap is dark
        /// </summary>
        Additive: int;
    }

/// <summary>
/// Based on Q3: drawVert_t
/// DrawVertex
/// </summary>
type DrawVertex =
    {
        Vertex: Vector3;
        St0: single;
        St1: single;
        Lightmap0: single;
        Lightmap1: single;
        Normal: Vector3;
        Color: Rgba;
    }

/// <summary>
/// Based on Q3: polyVert_t
/// PolyVertex
/// </summary>
type PolyVertex =
    {
        Vertex: Vector3;
        St0: single;
        St1: single;
        Modulate0: byte;
        Modulate1: byte;
        Modulate2: byte;
        Modulate3: byte;
    }

/// <summary>
/// Based on Q3: srfPoly_t
/// SurfacePoly
///
/// when cgame directly specifies a polygon, it becomes a srfPoly_t
/// as soon as it is called
/// </summary>
type SurfacePoly =
    {
        ShaderHandle: int;
        FogIndex: int;
        Vertices: PolyVertex list;
    }

/// <summary>
/// Based on Q3: srfDisplayList_t
/// SurfaceDisplayList
/// </summary>
type SurfaceDisplayList =
    { ListId: int }

/// <summary>
/// Based on Q3: srfFlare_t
/// SurfaceFlare
/// </summary>
type SurfaceFlare =
    { Origin: Vector3; Normal: Vector3; Color: Vector3 }

/// <summary>
/// Based on Q3: srfGridMesh_t
/// SurfaceGridMesh
/// </summary>
type SurfaceGridMesh =
    {
        DlightBit0: int;
        DlightBit1: int;

        // culling information
        MeshBounds: Bounds;
        LocalOrigin: Vector3;
        MeshRadius: single;

        // lod information, which may be different
        // than the culling information to allow for
        // groups of curves that LOD as a unit
        LodOrigin: Vector3;
        LodRadius: single;
        LodFixed: int;
        LodStitched: int;

        Width: int;
        Height: int;
        WidthLodError: single list;
        HeightLodError: single list;

        /// <summary>
        /// variable sized
        /// </summary>
        Vertex: DrawVertex;
    }

/// <summary>
/// FaceVertexPoints
/// </summary>
type FaceVertexPoints =
    {
        Vertex0: single;
        Vertex1: single;
        Vertex2: single;
        Vertex3: single;
        Vertex4: single;
        Vertex5: single;
        Vertex6: single;
        Vertex7: single;
    }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.Vertex0
            | 1 -> this.Vertex1
            | 2 -> this.Vertex2
            | 3 -> this.Vertex3
            | 4 -> this.Vertex4
            | 5 -> this.Vertex5
            | 6 -> this.Vertex6
            | 7 -> this.Vertex7
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Based on Q3: srfSurfaceFace_t
/// SurfaceFace
/// </summary>
type SurfaceFace =
    {
        Plane: Plane;
        DlightBit0: int;
        DlightBit1: int;

        // triangle definitions (no normals at points)
        PointCount: int;
        IndexCount: int;
        OffsetIndices: int;

        /// <summary>
        /// // variable sized; // there is a variable length list of indices here also
        /// </summary>
        Points: FaceVertexPoints;
    }

/// <summary>
/// Based on Q3: srfTriangles_t
/// SurfaceTriangles
///
/// // misc_models in maps are turned into direct geometry by q3map
/// </summary>
type SurfaceTriangles =
    {
        DlightBit0: int;
        DlightBit1: int;

        // culling information (FIXME: use this!)
        Bounds: Bounds;
        LocalOrigin: Vector3;
        Radius: single;

        // triangle definitions
        Indices: int list;
        Vertices: DrawVertex list;
    }

/// <summary>
/// Based on Q3: surfaceType_t
/// Surface
///
/// any changes in surfaceType must be mirrored in rb_surfaceTable[] // FIXME: Oh crap..
/// </summary>
type Surface =
    | Bad
    | Skip                              // ignore
    | Face of SurfaceFace
    | Grid of SurfaceGridMesh
    | Triangles of SurfaceTriangles
    | Poly of SurfacePoly
    | Md3
    | Md4
    | Flare of SurfaceFlare
    | Entity                            // beams, rails, lightning, etc that can be determined by entity
    | DisplayList of SurfaceDisplayList

/// <summary>
/// Based on Q3: drawSurf_t
/// DrawSurface
/// </summary>
type DrawSurface =
    {
        Sort: uint32;       // bit combination for fast compares
        Surface : Surface
    }

/// <summary>
/// Based on Q3: RDF_NOWORLDMODEL, RDF_HYPERSPACE
/// RdFlags
/// </summary>
[<Flags>]
type RdFlags =
    | NoWorldModel = 0x1    // used for player configuration screen
    | Hyperspace = 0x4      // teleportation effect

/// <summary>
/// Based on Q3: trRefdef_t
/// TrRefdef
///
/// trRefdef_t holds everything that comes in refdef_t,
/// as well as the locally generated scene information
/// </summary>
type TrRefdef =
    {
        X: int;
        Y: int;
        Width: int;
        Height: int;
        ViewOrigin: Vector3;
        ViewAxis: Axis;             // transformation matrix
        Time: int;                  // time in milliseconds for shader effects and other time dependent rendering issues
        RdFlags: RdFlags;

        // 1 bits will prevent the associated area from rendering at all
        AreaMask: ByteString;
        HasAreaMaskModified: bool;  // qtrue if areamask changed since last scene

        FloatTime: single;          // tr.refdef.time / 1000.0
        Text: string list;
        Entities: TrRefEntity list;
        Dlights: Dlight list;
        Polys: Surface list;
        DrawSurfaces: DrawSurface list;
    }

/// <summary>
/// Based on Q3: image_t
/// Image
/// </summary>
type Image =
    {
        Path : string;          // game path, including extension
        Width : int;
        Height : int;

        // after power of two and picmip but not including clamp to MAX_TEXTURE_SIZE
        UploadWidth : int;
        UploadHeight : int

        TextureId : int;        // gl texture binding // TODO: Perhaps we should have GL specific types
        FrameUsed : int;
        InternalFormat : int;
        IsMipmap : bool;
        CanAllowPicmip : bool;
        WrapClampMode: int;     // GL_CLAMP or GL_REPEAT
        
        Next: Image option; // Is this a good idea?
    }

/// <summary>
/// Based on Q3: dshader_t
/// DShader
/// </summary>
type DShader =
    {
        Shader: string;
        SurfaceFlags: int;
        ContentFlags: int;
    }

/// <summary>
/// Based on Q3: cullType_t
/// CullType
/// </summary>
type CullType =
    | FrontSided = 0
    | BackSided = 1
    | TwoSided = 2

/// <summary>
/// Based on Q3: fogPass_t
/// FogType
/// </summary>
type FogType =
    | None = 0  // surface is translucent and will just be adjusted properly
    | Equal = 1 // surface is opaque but possibly alpha tested
    | Le = 2    // surface is trnaslucent, but still needs a fog pass (fog surface)

/// <summary>
/// Skybox
/// </summary>
type Skybox =
    {
        Image0: Image;
        Image1: Image;
        Image2: Image;
        Image3: Image;
        Image4: Image;
        Image5: Image;
    }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.Image0
            | 1 -> this.Image1
            | 2 -> this.Image2
            | 3 -> this.Image3
            | 4 -> this.Image4
            | 5 -> this.Image5
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Based on Q3: skyParms_t
/// SkyParms
/// </summary>
type SkyParms =
    {
        CloudHeight: single;
        Outerbox: Skybox option;
        Innerbox: Skybox option;
    }

/// <summary>
/// Based on Q3: fogParms_t
/// FogParms
/// </summary>
type FogParms =
    {
        Color: Vector3;
        DepthForOpaque: single;
    }

/// <summary>
/// Based on Q3: genFunc_t
/// WaveFormType
/// </summary>
type WaveFormType =
    | None = 0
    | Sin = 1
    | Square = 2
    | Triangle = 3
    | Sawtooth = 4
    | InverseSawtooth = 5
    | Noise = 6

/// <summary>
/// Based on Q3: waveForm_t
/// WaveForm
/// </summary>
type WaveForm =
    {
        Type: WaveFormType;
        Base: single;
        Amplitude: single;
        Phase: single;
        Frequency: single;
    }

/// <summary>
/// Based on Q3: deform_t
/// DeformType
/// </summary>
type DeformType =
    | None = 0
    | Wave = 1
    | Normals = 2
    | Bulge = 3
    | Move = 4
    | ProjectionShadow = 5
    | Autosprite = 6
    | Autosprite2 = 7
    | Text0 = 8
    | Text1 = 9
    | Text2 = 10
    | Text3 = 11
    | Text4 = 12
    | Text5 = 13
    | Text6 = 14
    | Text7 = 15

/// <summary>
/// Based on Q3: deformStage_t
/// DeformStage
/// </summary>
type DeformStage =
    {
        Type: DeformType;       // vertex coordinate modification type
        MoveVector: Vector3;
        Wave: WaveForm;
        Spread: single;
        BulgeWidth: single;
        BulgeHeight: single;
        BulgeSpeed: single;
    }

/// <summary>
/// Based on Q3: texCoordGen_t
/// TextureCoordinateType
/// </summary>
type TextureCoordinateType =
    | Bad = 0
    | Identity = 1          // clear to 0,0
    | Lightmap = 2
    | Texture = 3
    | EnvironmentMapped = 4
    | Fog = 5
    | Vector = 6            // S and T from world coordinates

/// <summary>
/// TextureCoordinateVectors
/// </summary>
type TextureCoordinateVectors =
    { X: Vector3; Y: Vector3 }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.X
            | 1 -> this.Y
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Based on Q3: texMod_t
/// TextureModification
/// </summary>
type TextureModificationType =
    | None = 0
    | Transform = 1
    | Turbulent = 2
    | Scroll = 3
    | Scale = 4
    | Stretch = 5
    | Rotate = 7
    | EntityTranslate = 7

/// <summary>
/// Based on Q3: texModInfo_t
/// TextureModification
/// </summary>
type TextureModification =
    {
        Type: TextureModificationType;

        // used for TMOD_TURBULENT and TMOD_STRETCH
        Wave: WaveForm;

        // used for TMOD_TRANSFORM
        Matrix: Matrix2x2;        // s' = s * m[0][0] + t * m[1][0] + trans[0]
        Translate: Vector2;     // t' = s * m[0][1] + t * m[0][1] + trans[1]

        // used for TMOD_SCALE
        Scale: Vector2;         // s *= scale[0]
                                // t *= scale[1]

        // used for TMOD_SCROLL
        Scroll: Vector2;        // s' = s + scroll[0] * time
                                // t' = t + scroll[1] * time

        // + = clockwise
        // - = counterclockwise
        RotateSpeed: single;
    }

/// <summary>
/// Based on Q3: textureBundle_t
/// TextureBundle
/// </summary>
type TextureBundle =
    {
        ImageAnimations: Image seq;
        ImageAnimationSpeed: single;
        TextureCoordinateType: TextureCoordinateType;
        TextureCoordinateVectors: TextureCoordinateVectors;
        TextureModifications: TextureModification seq;
        VideMapHandle: int;
        IsLightmap: bool;
        IsVertexLightmap: bool;
        IsVideoMap: bool;
    }

/// <summary>
/// Based on Q3: colorGen_t
/// ShaderColorType
/// </summary>
type ShaderColorType =
    | Bad = 0
    | IdentityLighting = 1  // tr.identityLight
    | Identity = 2          // always (1,1,1,1)
    | Entity = 3            // grabbed from entity's modulate field
    | OneMinusEntity = 4    // grabbed from 1 - entity.modulate
    | ExactVertex = 5       // tess.vertexColors
    | Vertex = 6            // tess.vertexColors * tr.identityLight
    | OneMinuxVertex = 7
    | Waveform = 8          // programmatically generated
    | LightingDiffuse = 9
    | Fog = 10              // standard fog
    | Const = 11            // fixed color

/// <summary>
/// Based on Q3: alphaGen_t
/// ShaderAlphaType
/// </summary>
type ShaderAlphaType =
    | Identity = 0
    | Skip = 1
    | Entity = 2
    | OneMinusEntity = 3
    | Vertex = 4
    | OneMinusVertex = 5
    | LightingSpecular = 6
    | Waveform = 7
    | Portal = 8
    | Const = 9

/// <summary>
/// Based on Q3: acff_t (acff stands for adjustColorForFog .. lol)
/// FogColorType
/// </summary>
type FogColorType =
    | None = 0
    | Rgb = 1
    | Rgba = 2
    | Alpha = 3

/// <summary>
/// Based on Q3: shaderStage_t
/// ShaderStage
/// </summary>
type ShaderStage =
    {
        Active: bool;
        TextureBundle1: TextureBundle;
        TextureBundle2: TextureBundle;
        RgbWave: WaveForm;
        RgbColorType: ShaderColorType;
        AlphaWave: WaveForm;
        AlphaType: ShaderAlphaType;
        ConstantColor: Rgba;            // for CGEN_CONST and AGEN_CONST
        StateBits: int;                 // GLS_xxxx mask
        FogColorType: FogColorType;
        IsDetail: bool;
    }

/// <summary>
/// Based on Q3: shader_t
/// Shader
/// </summary>
type Shader =
    {
        Name: string;               // game path, including extension
        LightmapIndex: int;         // for a shader to match, both name and lightmapIndex must match
        Index: int;                 // this shader == tr.shaders[index]
        SortedIndex: int;           // this shader == tr.sortedShaders[sortedIndex]
        Sort: single;               // lower numbered shaders draw before higher numbered

        // we want to return index 0 if the shader failed to
        // load for some reason, but R_FindShader should
        // still keep a name allocated for it, so if
        // something calls RE_RegisterShader again with
        // the same name, we don't try looking for it again
        IsDefaultShader: bool;

        IsExplicitlyDefined: bool;  // found in a .shader file
        SurfaceFlags: int;          // if explicitlyDefined, this will have SURF_* flags
        ContentFlags: int;
        IsEntityMergable: bool;     // merge across entites optimizable (smoke, blood)
        IsSky: bool;
        Sky: SkyParms;
        Fog: FogParms;
        PortalRange: single;        // distance to fog out at
        MultitextureEnv: int;       // 0, GL_MODULATE, GL_ADD (FIXME: put in stage)
        CullType: CullType;         // CT_FRONT_SIDED, CT_BACK_SIDED, or CT_TWO_SIDED
        HasPolygonOffset: bool;     // set for decals and other items that must be offset 
        HasNoMipMaps: bool;         // for console fonts, 2D elements, etc.
        HasNoPicMip: bool;          // for images that must always be full resolution
        FogType: FogType;           // draw a blended pass, possibly with depth test equals
        NeedsNormal: bool;          // not all shaders will need all data to be gathered
        NeedsSt1: bool;
        NeedsSt2: bool;
        NeedsColor: bool;
        Deforms: DeformStage seq;
        Stages: ShaderStage seq;
        // void (*optimimalStageIteratorFunc)( void ); <-- TODO: Need to figure what to do with this guy.
        ClampTime: single;              // time this shader is clamped to
        TimeOffset: single;             // current time offset for this shader

        // Is StateId a better name vs. numStates?
        StateId: int;                   // if non-zero this is a state shader
        CurrentShader: Shader option;   // current state if this is a state shader
        ParentShader: Shader option;    // current state if this is a state shader
        CurrentState: int;              // current state index for cycle purposes
        ExpireTime: int64;              // time in milliseconds this expires
        RemappedShader: Shader option;  // current shader this one is remapped too
        ShaderStates: int seq;          // index to valid shader states
        Next: Shader option;
    }

/// <summary>
/// Based on Q3: msurface_t
/// DShader
/// </summary>
type MSurface =
    {
        ViewCount: int;         // if == tr.viewCount, already added
        Shader: Shader option;
        FogIndex: int;
        Data: Surface option;   // any of srf*_t
    }

/// <summary>
/// Based on Q3: bmodel_t
/// BModel
/// </summary>
type BModel =
    {
        Bounds: Bounds;         // for culling
        Surfaces: MSurface seq;
    }

/// <summary>
/// Based on Q3: mnode_t
/// MNode
/// </summary>
type MNode =
    {
        // common with leaf and node
        Contents: int;          // -1 for nodes, to differentiate from leafs
        VisFrame: int;          // node needs to be traversed if current

        // for bounding box culling
        Mins: Vector3;
        Maxs: Vector3;
        Parent: MNode option;

        // node specific
        Plane: Plane option;
        Child1: MNode option;
        Child2: MNode option;

        // leaf specific
        Cluster: int;
        Area: int;

        MarkSurfaces: MSurface seq;
    }

/// <summary>
/// Based on Q3: fog_t
/// Fog
/// </summary>
type Fog =
    {
        OriginalBrushId: int;
        Bounds: Bounds;
        Color: Rgba;                    // in packed byte format
        TextureCoordinateScale: single; // texture coordinate vector scales
        Parms: FogParms;

        // for clipping distance in fog when outside
        HasSurface: bool;
        Surface: Vector4;
    }

type LightGridBounds =
    { Bounds0: int; Bounds1: int; Bounds2: int }

    member inline this.Item
        with get (i) =
            match i with
            | 0 -> this.Bounds0
            | 1 -> this.Bounds1
            | 2 -> this.Bounds2
            | _ -> raise <| IndexOutOfRangeException ()

/// <summary>
/// Based on Q3: world_t
/// World
/// </summary>
type World =
    {
        Name: string;           // ie: maps/tim_dm2.bsp
        BaseName: string;       // ie: tim_dm2
        DataSize: int;
        Shaders: DShader seq;
        BModels: BModel seq;
        Planes: Plane seq;
        Nodes: MNode seq;
        Surfaces: MSurface seq;
        MarkSurfaces: MSurface seq;
        Fogs: Fog seq;
        LightGridOrigin: Vector3;
        LightGridSize: Vector3;
        LightGridInverseSize: Vector3;
        LightGridBounds: LightGridBounds;
        LightGridData: byte option; // FIXME: this right? byte *lightGridData
        ClusterCount: int;
        ClusterByteCount: int;

        // FIXME: I dont think this is right, looks like it may be just data. We'll see.
        Vis: byte option;           // may be passed in by CM_LoadMap to save space
        NoVis: byte option;         // clusterBytes of 0xff

        EntityString: string;
        EntityParsePoint: string;
    }

/// <summary>
/// Based on Q3: md3Header_t
/// Md3Header
/// </summary>
type Md3Header =
    {
        Id: int;
        Version: int;

        /// <summary>
        /// model name
        /// </summary>
        Name: string;
        Flags: int;
        FrameCount: int;
        TagCount: int;
        SurfaceCount: int;
        SkinCount: int;

        /// <summary>
        /// first surface
        /// </summary>
        FrameOffset: int;

        /// <summary>
        /// numFrames * numTags
        /// </summary>
        TagOffset: int;

        /// <summary>
        /// first surface, others follow
        /// </summary>
        SurfaceOffset: int;

        /// <summary>
        /// end of file
        /// </summary>
        EndOffset: int;
    }

/// <summary>
/// Based on Q3: md4Header_t
/// M43Header
/// </summary>
type Md4Header =
    {
        Id: int;
        Version: int;

        /// <summary>
        /// model name
        /// </summary>
        Name: string;

        // frames and bones are shared by all levels of detail
        Framecount: int;
        BoneCount: int;
        BoneNameOffset: int;    // char name[ MAX_QPATH ] 
        FrameOffset: int;       // md4Frame_t[numFrames]

        // each level of detail has completely separate sets of surfaces
        LodCount: int;
        LodOffset: int;
        EndOffset: int;         // end of file
    }

/// <summary>
/// Based on Q3: modtype_t
/// ModelType
/// </summary>
type ModelType =
    | Bad = 0
    | Brush = 1
    | Mesh = 2
    | Md4 = 3

/// <summary>
/// Based on Q3: model_t
/// Model
/// </summary>
type Model =
    {
        Name: string;
        Type: ModelType;
        Index: int;
        DataSize: int;
        BModel: BModel option;
        Md3: Md3Header;

    }

/// <summary>
/// Based on Q3: refdef_t
/// Refdef
/// </summary>
type Refdef =
    {
        X: int;
        Y: int;
        Width: int;
        Height: int;
        ViewOrigin: Vector3;
        ViewAxis: Axis;         // transformation matrix

        // time in milliseconds for shader effects and other time dependent rendering issues
        Time: int;
        RdFlags: RdFlags;
        // TODO:
    }

/// <summary>
/// Based on Q3: trGlobals_t
/// TrGlobals
///
/// Most renderer globals are defined here.
/// backend functions should never modify any of these fields,
/// but may read fields that aren't dynamically modified
/// by the frontend.
/// !!
/// TODO: Not finished.
/// !!
/// </summary>
type TrGlobals =
    {
        CurrentEntity: TrRefEntity option;
        CurrentEntityId: int;

        ViewParms: ViewParms;
        Refdef: TrRefdef;
        Orientation: OrientationR;
    }

[<CompilationRepresentation (CompilationRepresentationFlags.ModuleSuffix)>]
module TrGlobals =
    let updateCurrentEntityById entityId (tr: TrGlobals) =
        let entity = tr.Refdef.Entities.[entityId]
        { tr with CurrentEntity = Some entity; CurrentEntityId = entityId }
        