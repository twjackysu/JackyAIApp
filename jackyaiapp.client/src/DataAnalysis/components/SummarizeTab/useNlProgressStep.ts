import { useEffect, useState } from 'react';

/** When the progress moves to the second and the third of `NL_STEPS`, in ms after the request
 * starts. */
const STEP_TIMES_MS = [2200, 5500];

/**
 * Which of the staged progress steps the NL agent is on. The steps mirror the backend's real phases
 * (fetch live values → plan → execute); the timing is simulated locally since the request is a
 * single HTTP call. Starts over at 0 each time a request begins; keeps counting while the tab is in
 * the other mode.
 */
export function useNlProgressStep(nlIsLoading: boolean): number {
  const [step, setStep] = useState(0);
  useEffect(() => {
    if (!nlIsLoading) return;
    setStep(0);
    const timers = STEP_TIMES_MS.map((ms, i) => setTimeout(() => setStep(i + 1), ms));
    return () => timers.forEach(clearTimeout);
  }, [nlIsLoading]);
  return step;
}
