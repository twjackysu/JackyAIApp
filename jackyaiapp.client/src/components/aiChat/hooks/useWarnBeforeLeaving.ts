import { useEffect } from 'react';

/**
 * While `active`, reloading or closing the page asks the browser's own "leave this site?" first. The
 * assistant's confirmation card waits in its turn's own request (D-026), so leaving would drop the
 * question and do nothing the card asked about; the reader may still leave (D-105). Moving within the
 * site does not unload the page, so it is not asked about.
 */
export function useWarnBeforeLeaving(active: boolean) {
  useEffect(() => {
    if (!active) return;
    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Older browsers show the dialog only when returnValue is set; its text is the browser's own.
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [active]);
}
