"use client";

import { useRef, useState, type PointerEvent } from "react";
import { Button } from "@/components/ui/button";

export function SignaturePad({ onChange }: { onChange: (blob: Blob | null) => void }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const drawing = useRef(false);
  const [hasDrawn, setHasDrawn] = useState(false);

  function getContext() {
    return canvasRef.current?.getContext("2d") ?? null;
  }

  function pointerDown(e: PointerEvent<HTMLCanvasElement>) {
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    drawing.current = true;
    const rect = canvasRef.current.getBoundingClientRect();
    ctx.beginPath();
    ctx.moveTo(e.clientX - rect.left, e.clientY - rect.top);
  }

  function pointerMove(e: PointerEvent<HTMLCanvasElement>) {
    if (!drawing.current) return;
    const ctx = getContext();
    if (!ctx || !canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    ctx.lineTo(e.clientX - rect.left, e.clientY - rect.top);
    ctx.stroke();
    setHasDrawn(true);
  }

  function pointerUp() {
    if (!drawing.current) return;
    drawing.current = false;
    canvasRef.current?.toBlob((blob) => onChange(blob), "image/png");
  }

  function clear() {
    const ctx = getContext();
    if (ctx && canvasRef.current) {
      ctx.clearRect(0, 0, canvasRef.current.width, canvasRef.current.height);
    }
    setHasDrawn(false);
    onChange(null);
  }

  return (
    <div className="space-y-2">
      <canvas
        ref={canvasRef}
        role="img"
        aria-label="Signature pad"
        width={320}
        height={120}
        className="touch-none rounded-md border border-input bg-background"
        onPointerDown={pointerDown}
        onPointerMove={pointerMove}
        onPointerUp={pointerUp}
        onPointerLeave={pointerUp}
      />
      <Button type="button" variant="outline" size="sm" disabled={!hasDrawn} onClick={clear}>
        Clear
      </Button>
    </div>
  );
}
