import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from '@/apis/accountApis';

interface Props {
  children: React.ReactNode;
  /** Shown to signed-out visitors instead of redirecting to Google. Never rendered while the
   * auth check is still in flight. */
  fallback?: React.ReactNode;
}

function RequireAuth({ children, fallback }: Props) {
  const location = useLocation();
  const { data, isFetching, isError } = useGetUserInfoQuery();

  useEffect(() => {
    // If not fetching and either error (401) or no user data, redirect to login
    if (!fallback && !isFetching && (isError || !data?.data)) {
      window.location.replace(
        `/api/account/login/Google?ReturnUrl=${encodeURIComponent(location.pathname)}`,
      );
    }
  }, [data, isError, isFetching, location, fallback]);

  // Show nothing while checking auth or if not authenticated
  if (isFetching) {
    return null;
  }
  if (isError || !data?.data) {
    return fallback ?? null;
  }

  return children;
}

export default RequireAuth;
