"use client";

import { useEffect, useRef, useState } from "react";

interface Props {
  images: string[];
  alt: string;
  /** ms between automatic slides; auto-advance is disabled with a single image. */
  interval?: number;
  className?: string;
}

/**
 * Touch-swipeable image gallery that also advances on its own, so a customer
 * sees every photo of a dish while reading its description. Native scroll-snap
 * gives the swipe; a timer nudges to the next slide and pauses while the user
 * is interacting so it never fights the finger.
 */
export default function ImageCarousel({ images, alt, interval = 3200, className = "" }: Props) {
  const trackRef = useRef<HTMLDivElement>(null);
  const [index, setIndex] = useState(0);
  const pausedUntil = useRef(0);
  const count = images.length;

  // Keep `index` in sync with the user's manual scrolling.
  useEffect(() => {
    const el = trackRef.current;
    if (!el) return;
    let raf = 0;
    const onScroll = () => {
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => {
        const w = el.clientWidth || 1;
        setIndex(Math.round(el.scrollLeft / w));
      });
    };
    el.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      el.removeEventListener("scroll", onScroll);
      cancelAnimationFrame(raf);
    };
  }, []);

  // Pause auto-advance briefly whenever the user touches the gallery.
  useEffect(() => {
    const el = trackRef.current;
    if (!el) return;
    const pause = () => { pausedUntil.current = Date.now() + 6000; };
    el.addEventListener("pointerdown", pause, { passive: true });
    el.addEventListener("touchstart", pause, { passive: true });
    return () => {
      el.removeEventListener("pointerdown", pause);
      el.removeEventListener("touchstart", pause);
    };
  }, []);

  // Auto-advance loop.
  useEffect(() => {
    if (count <= 1) return;
    const id = setInterval(() => {
      if (Date.now() < pausedUntil.current) return;
      const el = trackRef.current;
      if (!el) return;
      const w = el.clientWidth || 1;
      const next = (Math.round(el.scrollLeft / w) + 1) % count;
      el.scrollTo({ left: next * w, behavior: "smooth" });
    }, interval);
    return () => clearInterval(id);
  }, [count, interval]);

  function goTo(i: number) {
    const el = trackRef.current;
    if (!el) return;
    pausedUntil.current = Date.now() + 6000;
    el.scrollTo({ left: i * el.clientWidth, behavior: "smooth" });
  }

  if (count === 0) {
    return (
      <div
        className={`m-accent-text flex items-center justify-center text-4xl opacity-40 ${className}`}
        style={{ background: "var(--m-border)" }}
      >
        ◆
      </div>
    );
  }

  return (
    <div className={`relative ${className}`}>
      <div
        ref={trackRef}
        className="flex h-full w-full snap-x snap-mandatory overflow-x-auto scrollbar-none"
        style={{ scrollSnapType: "x mandatory" }}
      >
        {images.map((src, i) => (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            key={i}
            src={src}
            alt={`${alt} ${i + 1}`}
            className="h-full w-full flex-shrink-0 snap-center object-cover"
            style={{ width: "100%" }}
            draggable={false}
          />
        ))}
      </div>

      {count > 1 && (
        <>
          {/* Dots */}
          <div className="pointer-events-auto absolute bottom-3 left-1/2 flex -translate-x-1/2 items-center gap-1.5 rounded-full bg-black/35 px-2.5 py-1.5 backdrop-blur">
            {images.map((_, i) => (
              <button
                key={i}
                aria-label={`Imagen ${i + 1}`}
                onClick={() => goTo(i)}
                className="h-2 rounded-full transition-all"
                style={{
                  width: i === index ? 18 : 8,
                  background: i === index ? "#fff" : "rgba(255,255,255,.55)",
                }}
              />
            ))}
          </div>
          {/* Counter (left side, so it never collides with a close button) */}
          <div className="absolute left-3 top-3 rounded-full bg-black/40 px-2.5 py-1 text-xs font-semibold text-white backdrop-blur">
            {index + 1}/{count}
          </div>
        </>
      )}
    </div>
  );
}
