"use client";

import { useEffect } from "react";
import { MapContainer, TileLayer, Marker, Popup } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { RiderLocation } from "@/features/rider-tracking/hooks/useRiderLocations";

const DEFAULT_CENTER: [number, number] = [27.7172, 85.324]; // Kathmandu — used only when no rider has reported yet.

function minutesAgo(recordedAt: string): number {
  return Math.max(0, Math.round((Date.now() - new Date(recordedAt).getTime()) / 60_000));
}

export function LiveMap({ locations, riderLicenseById }: { locations: RiderLocation[]; riderLicenseById: Map<string, string> }) {
  useEffect(() => {
    // Leaflet's default marker icon references image paths relative to the installed package,
    // which bundlers rewrite incorrectly — fixed once here with CDN URLs, the standard workaround.
    L.Icon.Default.mergeOptions({
      iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
      iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
      shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
    });
  }, []);

  const center: [number, number] = locations.length > 0 ? [locations[0].latitude, locations[0].longitude] : DEFAULT_CENTER;

  return (
    <MapContainer center={center} zoom={13} className="h-[500px] w-full rounded-md">
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {locations.map((location) => (
        <Marker key={location.riderId} position={[location.latitude, location.longitude]}>
          <Popup>
            <div>{riderLicenseById.get(location.riderId) ?? "Unknown rider"}</div>
            <div>Last seen {minutesAgo(location.recordedAt)}m ago</div>
          </Popup>
        </Marker>
      ))}
    </MapContainer>
  );
}
