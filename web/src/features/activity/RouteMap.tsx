import { useEffect, useMemo, type ReactNode } from 'react';
import { CircleMarker, MapContainer, Polyline, TileLayer, useMap } from 'react-leaflet';
import type { LatLngTuple } from 'leaflet';
import 'leaflet/dist/leaflet.css';
import type { RouteDto } from '../../api/types';

/** A fixed pair, unlike Leaflet's LatLngBoundsLiteral, so both corners destructure. */
type Bounds = [LatLngTuple, LatLngTuple];

/** GeoJSON gives [longitude, latitude]; Leaflet wants [latitude, longitude]. */
function toLatLngs(coordinates: number[][]): LatLngTuple[] {
  const positions: LatLngTuple[] = [];

  for (const pair of coordinates) {
    const longitude = pair[0];
    const latitude = pair[1];
    if (typeof longitude === 'number' && typeof latitude === 'number' && Number.isFinite(longitude) && Number.isFinite(latitude)) {
      positions.push([latitude, longitude]);
    }
  }

  return positions;
}

/** The bounding box is [minLon, minLat, maxLon, maxLat] - also GeoJSON order. */
function toBounds(boundingBox: number[], fallback: LatLngTuple[]): Bounds | null {
  const [minLon, minLat, maxLon, maxLat] = boundingBox;

  if (
    typeof minLon === 'number' &&
    typeof minLat === 'number' &&
    typeof maxLon === 'number' &&
    typeof maxLat === 'number' &&
    Number.isFinite(minLon) &&
    Number.isFinite(minLat) &&
    Number.isFinite(maxLon) &&
    Number.isFinite(maxLat)
  ) {
    return [
      [minLat, minLon],
      [maxLat, maxLon],
    ];
  }

  if (fallback.length === 0) {
    return null;
  }

  let south = Number.POSITIVE_INFINITY;
  let west = Number.POSITIVE_INFINITY;
  let north = Number.NEGATIVE_INFINITY;
  let east = Number.NEGATIVE_INFINITY;

  for (const [latitude, longitude] of fallback) {
    south = Math.min(south, latitude);
    north = Math.max(north, latitude);
    west = Math.min(west, longitude);
    east = Math.max(east, longitude);
  }

  return [
    [south, west],
    [north, east],
  ];
}

function centreOf(bounds: Bounds): LatLngTuple {
  const [south, west] = bounds[0];
  const [north, east] = bounds[1];
  return [(south + north) / 2, (west + east) / 2];
}

/**
 * Leaflet measures its container once at construction. Inside a flex layout that
 * measurement happens before the panel has its final width, so the map has to be
 * told to re-measure before the bounds are fitted or the route lands off-centre.
 */
function FitRoute({ bounds }: { bounds: Bounds }): null {
  const map = useMap();

  useEffect(() => {
    map.invalidateSize();
    map.fitBounds(bounds, { padding: [28, 28] });
  }, [map, bounds]);

  useEffect(() => {
    const handleResize = (): void => {
      map.invalidateSize();
    };

    window.addEventListener('resize', handleResize);
    return () => {
      window.removeEventListener('resize', handleResize);
    };
  }, [map]);

  return null;
}

export function RouteMap({ route }: { route: RouteDto }): ReactNode {
  const positions = useMemo(() => toLatLngs(route.coordinates), [route.coordinates]);
  const bounds = useMemo(() => toBounds(route.boundingBox, positions), [route.boundingBox, positions]);

  if (bounds === null || positions.length < 2) {
    return <div className="map map--empty">This activity has no usable GPS track.</div>;
  }

  const start = positions[0];
  const finish = positions[positions.length - 1];

  return (
    <div className="map">
      <MapContainer
        className="map__canvas"
        center={centreOf(bounds)}
        zoom={13}
        scrollWheelZoom={false}
        attributionControl
      >
        <TileLayer
          url="https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>'
          maxZoom={19}
        />
        <Polyline positions={positions} pathOptions={{ color: '#4fd1c5', weight: 4, opacity: 0.95 }} />
        {start !== undefined ? (
          <CircleMarker center={start} radius={6} pathOptions={{ color: '#0f1720', fillColor: '#4ade80', fillOpacity: 1, weight: 2 }} />
        ) : null}
        {finish !== undefined ? (
          <CircleMarker center={finish} radius={6} pathOptions={{ color: '#0f1720', fillColor: '#f87171', fillOpacity: 1, weight: 2 }} />
        ) : null}
        <FitRoute bounds={bounds} />
      </MapContainer>
      <p className="map__caption">
        Drawing {String(route.simplifiedPointCount.toLocaleString())} simplified points of{' '}
        {String(route.pointCount.toLocaleString())} recorded.
      </p>
    </div>
  );
}
