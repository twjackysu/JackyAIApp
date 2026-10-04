import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { trackPageView } from '@/utils/analytics';

/** One anonymous page view per route change (SPA navigation makes no server request). */
function usePageViewTracking() {
  const { pathname } = useLocation();
  const { data, isLoading } = useGetUserInfoQuery();
  const isAuthenticated = !!data?.data;

  useEffect(() => {
    // Wait for the auth check so the first view carries the right isAuthenticated value.
    if (isLoading) return;
    void trackPageView(pathname, isAuthenticated);
    // isAuthenticated deliberately omitted: logging in/out is not a new page view.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pathname, isLoading]);
}

export default usePageViewTracking;
