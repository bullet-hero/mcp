namespace BH.MCP.Tools
{
    // Served by the server itself (guide_get topic "edit"), not rendered from the public docs: it is the
    // op reference edit_batch's description would otherwise carry on every connect.

    /// <summary> The full reference of <c>edit_batch</c>'s values, tracks and level-wide verbs. </summary>
    public static class EditGuide
    {
        /// <summary> The topic name <c>guide_get</c> answers locally. </summary>
        public const string Topic = "edit";

        /// <summary> The topic, with <c>\n</c> line ends whatever the file's own are. </summary>
        public static readonly string Text = Raw.Replace("\r\n", "\n").Trim();

        private const string Raw = """
# Guide: edit (the edit_batch reference)

## Times
A global frame from 1 (120, "f:120") or "s:12.5"; with a beat grid also "bar:17", "bar:17.3" (beat 3 of bar
17), "beat:64.5", "marker:drop". Any time moves along the grid ("bar:17+2beats", "marker:drop-1bar") or by
frames ("bar:17-1f": the last frame before bar 17). A marker an earlier levelkey of the same batch writes is
usable below it. Durations: 60, "f:60", "s:2", "2bars", "3beats" and sums ("1bars+1f"). A span is half-open,
so a key on the frame where it ends is never reached. Inside Prefab Mode only frames and seconds read, from 1
at the template's start.

## Tracks and values
Tracks: position, rotation, scale, size, anchorMin, anchorMax, pivot, color, uv, textFontSize, textAppearing,
textFillment. Rotation is in RADIANS (cursor_resolve reports radians too; generators take degrees).
Values are readable JSON with a kind, e.g. {"kind":"Vector2Value","x":3,"y":4}; a bare number is a plain float
(rotation 1.57). Copy the shape of a track's value from keys_get (forms, for a track with no keys).
textFontSize: a number is a fixed size in world units, {minValue, maxValue} a band the text shrinks into to fit
its rect. textAppearing {value, mode Forward|Backward|Random} is the share of characters HIDDEN behind the mask
(0 all shown, 1 all masked); textFillment {value, direction Forward|Backward|FromCenter|ToCenter} the share
written.
color: "#RRGGBBAA", {"theme":2}, or a gradient over the rect:
{"kind":"ColorVerticalKey","color4Bottom":..,"color4Top":..},
{"kind":"ColorHorizontalKey","color4Left":..,"color4Right":..},
{"kind":"Color4X4Key","color4BL":..,"color4BR":..,"color4TL":..,"color4TR":..}.
Random values: FloatMinMax, Vector2Rect, Vector2Circle, Vector2Lerp, Color4MinMax, Color4Lerp (keys_get shows
their members).
ease is an EaseType name (Linear, Constant, InSine, InOutSine, OutQuad, ...); the ease of the LATER key of a pair
shapes the move into it. Keys at the extremes only with InOutSine make an exact sine: a seamless loop.
A key outside its object's span is never reached.

## Shapes
A catalog shape keeps its proportions inside the rect (shapes_find says what it draws: a Triangle at size
(2,2) is 2 wide, 1.73 tall), except Square and Circle, which stretch: a Square at size (4, 0.2) turned by
rotation is any bar or line, a Circle at (2, 1) an ellipse. A polygon fills its rect. Prefer the catalog;
a polygon only where no catalog shape fits, with its real corners only.

## Themes
theme: ref "$th", [from: game or level theme to copy, default the game's first], [name], [slots
{"2":"#RRGGBBAA",...}] makes a level theme; or id (theme guid or name), [name], [slots] edits one. Slots are
0-63, the n of {"theme":n}: 0 fallback, 1 GUI, 2 background, 3-6 players, 7 tail, 16-24 objects, 32-40
parallax, 48-56 effects. Activate it with levelkey game theme (value: guid, name or ref).

## Level keys
levelkey: owner, track, frame, value, [ease] / levelunkey: owner, track, frame; levelkeys_get shows a track's
keys and the value form it takes. A track's first key holds from frame 1 (a warning says so), so key the start
too when a change should begin later.
camera: position rotation (RADIANS) zoom pivot shake (zoom is the visible HEIGHT in world units, default 10, not
a multiplier). game: marker {name,description,color4} checkpoint {name,active,color4,position,space,
restoreHealth} screenLimit background ("#RRGGBB") theme. player: visible control collision size speed.
post: bloom chromatic vignette lens grain motionBlur colorCurves liftGammaGain shadowsMidtonesHighlights
whiteBalance analogGlitch digitalGlitch.

## Audio
audiotrack: ref "$t", start, [name, duration, layer 0-15, resource (id, or the file or preset name), offset (s),
speed, volume] (a resource with no duration lasts as long as its clip, cut at the level's end with a warning);
audioset: id, same optional fields; audiodelete: ids; audiokey: id, track volume|stereoPan, frame, value,
[ease]; audiounkey: id, track, frame. events_get audio lists a track's keys.

## Level meta and settings
meta: field name|description|tags|authors, value (tags and authors are lists; an author is a text or {name,
url, credit}). resourcemeta: kind texture|font|audio, id, [title], [description], [url], [sources] (texts),
[authors]: the attribution record of an imported file; given fields replace theirs. setting: field
framerate|duration (frames)|seed|orientation, value (framerate keeps frame numbers, so everything retimes).

## Prefabs
Anything that appears more than once is a prefab: build one, prefab_pack it, then create type prefab with
prefab: that ref (or the template guid) for every other copy, and key on a copy only what differs - its
position, scale, colours - so it becomes an override (prefab_overrides lists them). A placement takes one layer;
its content draws at the placement's layer plus its own and may share layers with the rest of the level.

## Layers
Layers are EFFECTIVE: the value you give is the sum up the parent chain. A higher layer draws in front. Two
objects never share an effective layer while both play: a batch lifts the later one to the next free layer
and says so in a warning, so give every object its own layer, the one on top the higher.
""";
    }
}
