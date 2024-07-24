import { useEffect } from 'react';
import { Route, RouteProps, useLocation, useNavigate } from 'react-router-dom';
import { useCheckAuthQuery } from '@/apis/accountApis';

function ProtectedRoute({ element, ...rest }: RouteProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const { data, isFetching, isError } = useCheckAuthQuery();

  useEffect(() => {
    if (!isFetching && !isError && !data?.isAuthenticated) {
      navigate(`/api/account/login/Google?ReturnUrl=${encodeURIComponent(location.pathname)}`, {
        replace: true,
      });
    }
  }, [data, isError, isFetching, location.pathname, navigate]);

  if (isFetching || isError || !data?.isAuthenticated) {
    // Optionally, render a loading indicator or null while checking authentication
    return null;
  }

  // Directly return a <Route> component with the protected element
  return <Route {...rest} element={element} />;
}

export default ProtectedRoute;
