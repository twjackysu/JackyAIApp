import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';

import { useGetUserInfoQuery } from '@/apis/accountApis';

interface Props {
  children: React.ReactNode;
}

function RequireAuth({ children }: Props) {
  const location = useLocation();
  const { data, isFetching, isError } = useGetUserInfoQuery();

  useEffect(() => {
    // If not fetching and either error (401) or no user data, redirect to login
    if (!isFetching && (isError || !data?.data)) {
      window.location.replace(
        `/api/account/login/Google?ReturnUrl=${encodeURIComponent(location.pathname)}`,
      );
    }
  }, [data, isError, isFetching, location]);

  // Show nothing while checking auth or if not authenticated
  if (isFetching || isError || !data?.data) {
    return null;
  }

  return children;
}

export default RequireAuth;
