/** Sends the visitor to Google sign-in, and back to the page they were on afterwards. */
export const signInAndComeBack = () => {
  window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(window.location.pathname)}`;
};
