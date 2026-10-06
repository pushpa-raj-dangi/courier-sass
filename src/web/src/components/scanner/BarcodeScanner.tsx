"use client";

import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { cn } from "@/lib/utils";
import { createDeduper, SCAN_DEDUPE_MS } from "./dedupe";

declare global {
  interface Window {
    BarcodeDetector?: new (options?: { formats?: string[] }) => {
      detect: (source: CanvasImageSource) => Promise<{ rawValue: string }[]>;
    };
  }
}

export function BarcodeScanner({
  onDetect,
  continuous = false,
  manualEntry = true,
}: {
  onDetect: (awb: string) => void;
  continuous?: boolean;
  manualEntry?: boolean;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [manualValue, setManualValue] = useState("");
  const [cameraError, setCameraError] = useState<string | null>(null);
  // Camera access is requested only after an explicit tap, never on mount — CLAUDE.md §33 and the
  // design spec's §7 security note both call this out: a rider opening /rider/scan shouldn't be
  // hit with a permission prompt before choosing to use the camera at all (manual entry is always
  // available with no prompt).
  const [scanning, setScanning] = useState(false);

  // Held in a ref (rather than read directly as an effect dependency) so a parent re-render that
  // produces a new onDetect reference — e.g. RiderScanPage re-rendering after setMessage(...) or
  // the re-render useRiderPickupScan's useMutation triggers — never restarts the scanning effect
  // below. Restarting mid-scan would re-request getUserMedia and reconstruct BarcodeDetector while
  // the same barcode is still in frame, firing a duplicate onDetect (and, on the rider Scan page, a
  // duplicate pickup-scan write with its own idempotency key) for one physical scan.
  const onDetectRef = useRef(onDetect);
  onDetectRef.current = onDetect;
  const continuousRef = useRef(continuous);
  continuousRef.current = continuous;
  const dedupeRef = useRef(createDeduper(SCAN_DEDUPE_MS));

  useEffect(() => {
    if (!scanning) return;
    let stream: MediaStream | null = null;
    let cancelled = false;
    let rafId: number | null = null;

    function stopScanning() {
      if (rafId) cancelAnimationFrame(rafId);
      rafId = null;
      stream?.getTracks().forEach((track) => track.stop());
      stream = null;
    }

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
                const code = results[0].rawValue.trim().toUpperCase();
                if (continuousRef.current) {
                  if (dedupeRef.current(code)) onDetectRef.current(code);
                } else {
                  onDetectRef.current(code);
                  // One physical scan, one detection: stop the camera (single-shot rider flow).
                  stopScanning();
                  if (!cancelled) setScanning(false);
                  return;
                }
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
            if (!result || cancelled) return;
            const code = result.getText().trim().toUpperCase();
            if (continuousRef.current) {
              if (dedupeRef.current(code)) onDetectRef.current(code);
              return;
            }
            onDetectRef.current(code);
            controls.stop();
            stopScanning();
            setScanning(false);
          });
        }
      } catch {
        setCameraError("Camera unavailable — use manual entry below.");
      }
    }

    void start();
    return () => {
      cancelled = true;
      stopScanning();
    };
  }, [scanning]);

  function submitManual() {
    const trimmed = manualValue.trim().toUpperCase();
    if (trimmed) onDetect(trimmed);
  }

  return (
    <div className="flex flex-col gap-4">
      <div className={cn("relative w-full overflow-hidden rounded-lg bg-black", continuous ? "aspect-video" : "aspect-square")}>
        {scanning ? (
          <video ref={videoRef} className="h-full w-full object-cover" muted playsInline />
        ) : (
          <div className="flex h-full w-full items-center justify-center">
            <Button type="button" size="lg" className="h-11" onClick={() => setScanning(true)}>{continuous ? "Start camera" : "Start Scanning"}</Button>
          </div>
        )}
        {scanning && continuous && (
          <Button type="button" variant="secondary" size="sm" className="absolute right-2 top-2" onClick={() => setScanning(false)}>
            Stop camera
          </Button>
        )}
        {cameraError && <p className="absolute inset-x-0 bottom-2 text-center text-sm text-white">{cameraError}</p>}
      </div>
      {manualEntry && (
        <div className="space-y-2">
          <Label htmlFor="manual-awb">Enter AWB manually</Label>
          <div className="flex gap-2">
            <Input id="manual-awb" className="h-11" value={manualValue} onChange={(e) => setManualValue(e.target.value)} />
            <Button type="button" className="h-11" onClick={submitManual}>Use this AWB</Button>
          </div>
        </div>
      )}
    </div>
  );
}
