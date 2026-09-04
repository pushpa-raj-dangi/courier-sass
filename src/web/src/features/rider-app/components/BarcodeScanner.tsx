"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

declare global {
  interface Window {
    BarcodeDetector?: new (options?: { formats?: string[] }) => {
      detect: (source: CanvasImageSource) => Promise<{ rawValue: string }[]>;
    };
  }
}

export function BarcodeScanner({ onDetect }: { onDetect: (awb: string) => void }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [manualValue, setManualValue] = useState("");
  const [cameraError, setCameraError] = useState<string | null>(null);
  // Camera access is requested only after an explicit tap, never on mount — CLAUDE.md §33 and the
  // design spec's §7 security note both call this out: a rider opening /rider/scan shouldn't be
  // hit with a permission prompt before choosing to use the camera at all (manual entry is always
  // available with no prompt).
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    if (!scanning) return;
    let stream: MediaStream | null = null;
    let cancelled = false;
    let rafId: number | null = null;

    async function start() {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } });
        if (cancelled || !videoRef.current) return;
        videoRef.current.srcObject = stream;
        await videoRef.current.play();

        if (window.BarcodeDetector) {
          const detector = new window.BarcodeDetector();
          const loop = async () => {
            if (cancelled || !videoRef.current) return;
            try {
              const results = await detector.detect(videoRef.current);
              if (results[0]?.rawValue) {
                onDetect(results[0].rawValue.trim().toUpperCase());
                return;
              }
            } catch {
              // transient decode failure — keep looping
            }
            rafId = requestAnimationFrame(loop);
          };
          rafId = requestAnimationFrame(loop);
        } else {
          const { BrowserMultiFormatReader } = await import("@zxing/browser");
          const reader = new BrowserMultiFormatReader();
          const controls = await reader.decodeFromVideoElement(videoRef.current, (result) => {
            if (result && !cancelled) {
              onDetect(result.getText().trim().toUpperCase());
              controls.stop();
            }
          });
        }
      } catch {
        setCameraError("Camera unavailable — use manual entry below.");
      }
    }

    void start();
    return () => {
      cancelled = true;
      if (rafId) cancelAnimationFrame(rafId);
      stream?.getTracks().forEach((track) => track.stop());
    };
  }, [scanning, onDetect]);

  function submitManual() {
    const trimmed = manualValue.trim().toUpperCase();
    if (trimmed) onDetect(trimmed);
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="relative aspect-square w-full overflow-hidden rounded-lg bg-black">
        {scanning ? (
          <video ref={videoRef} className="h-full w-full object-cover" muted playsInline />
        ) : (
          <div className="flex h-full w-full items-center justify-center">
            <Button type="button" size="lg" onClick={() => setScanning(true)}>Start Scanning</Button>
          </div>
        )}
        {cameraError && <p className="absolute inset-x-0 bottom-2 text-center text-sm text-white">{cameraError}</p>}
      </div>
      <div className="space-y-2">
        <Label htmlFor="manual-awb">Enter AWB manually</Label>
        <div className="flex gap-2">
          <Input id="manual-awb" value={manualValue} onChange={(e) => setManualValue(e.target.value)} />
          <Button type="button" onClick={submitManual}>Use this AWB</Button>
        </div>
      </div>
    </div>
  );
}
