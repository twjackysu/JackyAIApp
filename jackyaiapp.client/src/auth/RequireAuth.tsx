import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { useCheckAuthQuery } from '@/apis/accountApis';

interface Props {
  children: React.ReactNode;
}

function RequireAuth({ children }: Props) {
  const location = useLocation();
  const { data, isFetching, isError } = useCheckAuthQuery();

  useEffect(() => {
    if (!isFetching && !isError && !data?.isAuthenticated) {
      window.location.replace(
        `/api/account/login/Google?ReturnUrl=${encodeURIComponent(location.pathname)}`,
      );
    }
  }, [data, isError, isFetching, location]);
  if (isFetching || isError || !data?.isAuthenticated) {
    return null;
  }

  return children;
}

export default RequireAuth;
