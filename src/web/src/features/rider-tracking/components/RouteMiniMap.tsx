"use client";

import { CircleMarker, MapContainer, Polyline, TileLayer } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

/** Today's breadcrumb line: hollow start point, solid latest point. Needs at least one point. */
export function RouteMiniMap({ points }: { points: RiderLocation[] }) {
  const path = points.map((p) => [p.latitude, p.longitude] as [number, number]);
  const start = path[0];
  const latest = path[path.length - 1];
  const bounds = path.length > 1 ? L.latLngBounds(path) : undefined;

  return (
    <MapContainer
      center={bounds ? undefined : latest}
      zoom={15}
      bounds={bounds}
      boundsOptions={{ padding: [16, 16] }}
      scrollWheelZoom={false}
      className="h-48 w-full rounded-lg"
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {path.length > 1 && <Polyline positions={path} pathOptions={{ className: "stroke-primary", weight: 3 }} />}
      <CircleMarker center={start} radius={5} pathOptions={{ className: "fill-background stroke-primary", fillOpacity: 1, weight: 2 }} />
      <CircleMarker center={latest} radius={6} pathOptions={{ className: "fill-primary stroke-background", fillOpacity: 1, weight: 2 }} />
    </MapContainer>
  );
}
