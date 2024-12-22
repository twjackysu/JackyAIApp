import { matchPath, useLocation } from 'react-router-dom';

function useRouteMatch(patterns: readonly string[]) {
  const { pathname } = useLocation();

  // 檢查每個 pattern 是否匹配
  const matchedRoute = patterns.find((pattern) => {
    // 確保根路徑 "/" 完全匹配，其他路徑允許子路徑
    return matchPath({ path: pattern, end: pattern === '/' }, pathname);
  });

  return matchedRoute ? { pattern: { path: matchedRoute } } : null;

  // for (let i = 0; i < patterns.length; i += 1) {
  //   const pattern = patterns[i];
  //   const possibleMatch = matchPath(pattern, pathname);
  //   if (possibleMatch !== null) {
  //     return possibleMatch;
  //   }
  // }

  // return null;
}

export default useRouteMatch;
