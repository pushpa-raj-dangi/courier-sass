"use client";

import { useEffect } from "react";
import { MapContainer, Marker, Popup, TileLayer } from "react-leaflet";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { cn } from "@/lib/utils";
import { branchPinHtml, riderPinHtml, type MapBranchPin, type MapRiderPin } from "@/features/rider-tracking/lib/mapPins";

const DEFAULT_CENTER: [number, number] = [27.7172, 85.324]; // Kathmandu — only when nothing has a position.

export function LiveMap({ pins, branch, className }: { pins: MapRiderPin[]; branch?: MapBranchPin; className?: string }) {
  useEffect(() => {
    // Leaflet's default marker icon references image paths relative to the installed package,
    // which bundlers rewrite incorrectly — fixed once here with CDN URLs, the standard workaround.
    L.Icon.Default.mergeOptions({
      iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
      iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
      shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
    });
  }, []);

  const center: [number, number] = branch
    ? [branch.latitude, branch.longitude]
    : pins.length > 0
      ? [pins[0].latitude, pins[0].longitude]
      : DEFAULT_CENTER;

  return (
    <MapContainer center={center} zoom={13} className={cn("h-[480px] w-full rounded-lg", className)}>
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {branch && (
        <Marker
          position={[branch.latitude, branch.longitude]}
          title={branch.label}
          icon={L.divIcon({ className: "", iconSize: [64, 24], iconAnchor: [32, 12], html: branchPinHtml(branch.label) })}
        />
      )}
      {pins.map((pin) => (
        <Marker
          key={pin.riderId}
          position={[pin.latitude, pin.longitude]}
          title={pin.label}
          icon={L.divIcon({ className: "", iconSize: [40, 52], iconAnchor: [20, 16], html: riderPinHtml(pin) })}
        >
          <Popup>{pin.popup}</Popup>
        </Marker>
      ))}
    </MapContainer>
  );
}
