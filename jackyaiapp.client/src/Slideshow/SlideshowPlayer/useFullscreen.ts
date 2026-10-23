import { useCallback, useEffect, useState, type RefObject } from 'react';

/** Whether the page is in full screen, and toggling `target` into it or the page out of it. */
export function useFullscreen(target: RefObject<HTMLElement | null>) {
  const [isFullscreen, setIsFullscreen] = useState(false);

  const toggle = useCallback(() => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void target.current?.requestFullscreen();
  }, [target]);

  useEffect(() => {
    const onChange = () => setIsFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  return { isFullscreen, toggle };
}
