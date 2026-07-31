# Situation Markers Implementation Plan

## Goal

Add a situation marker system to the main Flight Data map to help the pilot control tactical context during flight. The feature is independent from mission planning: markers are pilot-side situational aids, not MAVLink mission items.

## Core Design

- Implement the feature in a separate `GCSViews/SituationMarkers` module.
- Keep `FlightData` integration thin:
  - create the marker manager after the map overlays are initialized;
  - add a map button in the lower-right corner;
  - forward map mouse events to the manager;
  - forward current drone position/altitude to the manager;
  - dispose/close marker UI when Flight Data is closed.

## Classes

- `SituationMarker`
  - marker data model;
  - stores coordinates, altitude, HOME flag, interest flag and altitude source.
- `SituationMarkersManager`
  - owns marker state;
  - owns GMap overlays;
  - synchronizes markers, route lines, autosave and map interaction.
- `SituationMarkersForm`
  - separate marker-management window;
  - contains marker table and action buttons.
- `ElevationProfileForm`
  - separate window for the route elevation profile.
- `ElevationProfileControl`
  - draws the SRTM terrain profile, route point lines, HOME/interest altitude lines and drone position line.
- `SituationMarkerMapMarker`
  - custom map marker with labels and interest highlighting.

## Marker Behavior

- `Add Marker` only adds a row to the table.
- A map marker is created only when both coordinates are known:
  - after manual coordinate entry; or
  - after `Pick on map` and a map click.
- `Add HOME Marker` uses only `MainV2.comPort.MAV.cs.HomeLocation`.
- `PlannedHomeLocation` must not be used.
- If HOME is unavailable or `0,0`, show a message and do not create/update the HOME marker.
- HOME marker is initialized from autopilot HOME only at creation/update time. It is not automatically moved when autopilot HOME changes later.
- If the pilot manually moves the HOME marker, all relative heights are recalculated from the new HOME marker position and altitude.

## Point Of Interest

- The point of interest is not a separate marker type.
- It is one selected marker from the table.
- Only one marker can be the point of interest.
- By default, the point of interest is the last valid non-HOME route marker.
- The point of interest must be highlighted on the map and in the table.

## Altitude Rules

- Marker altitude is stored as absolute altitude above mean sea level in meters.
- Initial marker altitude is loaded from SRTM.
- Operator can override altitude manually in the table.
- If altitude is manually edited, `AltitudeSource` becomes `Manual`.
- Manual altitude remains unchanged while coordinates stay unchanged.
- If coordinates change through table editing, map picking or dragging, altitude is reloaded from SRTM and `AltitudeSource` becomes `Srtm`.

## Map Interaction

- Markers can be dragged on the map.
- Dragging updates:
  - marker coordinates;
  - SRTM altitude;
  - table values;
  - route lines;
  - autosave.
- `Pick on map` enters a one-click coordinate selection mode for the selected row.
- After the map click, the mode turns off automatically.

## Route Lines

- Draw route lines between valid markers.
- HOME marker is used as the first route point when it exists.
- Other valid markers follow table order.
- Markers without both coordinates are ignored.

## Persistence

- Add autosave to a versioned JSON file.
- Add manual `Save` and `Load` buttons.
- JSON should store:
  - format version;
  - marker order;
  - coordinates;
  - altitude;
  - altitude source;
  - HOME flag;
  - selected interest marker.
- Autosave is triggered only by marker-related changes, not by telemetry updates.

## Elevation Profile

The elevation profile window draws:

- terrain height from SRTM along the composed route;
- HOME altitude horizontal line;
- point-of-interest altitude horizontal line;
- vertical lines for intermediate route markers;
- vertical line for current drone position projected onto the route when possible.

If there are fewer than two valid route points, the profile window should display an empty/insufficient-route state.

## Map Labels

- HOME marker label: `HOME`.
- Other marker labels: altitude relative to HOME, for example `+42 m`.
- Drone label:
  - altitude relative to HOME;
  - altitude relative to point of interest.

