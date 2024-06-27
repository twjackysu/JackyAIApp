import { useEffect } from 'react';

function SignIn() {
  const queryParams = new URLSearchParams(window.location.search);
  const redirectUrl = queryParams.get('ReturnUrl') || '';
  useEffect(() => {
    window.location.replace(
      `api/account/login/Google?ReturnUrl=${encodeURIComponent(redirectUrl)}`,
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return <div></div>;
}

export default SignIn;
