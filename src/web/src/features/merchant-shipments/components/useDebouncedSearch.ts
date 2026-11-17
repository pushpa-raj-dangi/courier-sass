"use client";

import { useEffect, useRef, useState } from "react";

/**
 * Local text for a search box that is committed to the URL value after `delay` ms.
 * An external change to `value` (back button, clear) replaces the text; our own write catching up does not.
 */
export function useDebouncedSearch(value: string, onCommit: (q: string) => void, delay = 300) {
  const [text, setText] = useState(value);
  const lastPushed = useRef(value);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const commitRef = useRef(onCommit);
  commitRef.current = onCommit;

  const clear = () => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
  };

  useEffect(() => {
    if (value !== lastPushed.current) {
      clear();
      lastPushed.current = value;
      setText(value);
    }
  }, [value]);
  useEffect(() => clear, []);

  function onChange(next: string) {
    setText(next);
    clear();
    if (next === lastPushed.current) return;
    timer.current = setTimeout(() => {
      timer.current = null;
      lastPushed.current = next;
      commitRef.current(next);
    }, delay);
  }

  return { text, onChange };
}
