import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { cloneElement } from 'react';
import { Link } from 'react-router-dom';

import BriefingCard from './BriefingCard';
import TodaySection from './TodaySection';

import type { AppDef } from '@/constants/apps';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { ENGLISH_LEARNING, FINANCE, PORTFOLIO, portalDestinations } from '@/constants/apps';

// Core apps first (Boss, DECISION-009); the rest stay fully visible right below, same card style.
const primaryApps: AppDef[] = [ENGLISH_LEARNING, PORTFOLIO, FINANCE];
const otherApps: AppDef[] = portalDestinations.filter((a) => !primaryApps.includes(a));

/** The hero's line for a guest: Studio AI is for signed-in readers only, so the apps are the way in. */
export const APPS_LINE =
  '每個應用都是獨立的工具。選好之後就會進入專屬的介面 — 想切換時再回到這個目錄即可。';

/**
 * The hero's line for a signed-in reader, who has Studio AI on every page (D-200): the directory is no longer the only
 * way between the apps, and the shortcut is otherwise only named in the button's hover hint.
 */
export const STUDIO_LINE =
  '在任何頁面按 ⌘K（Ctrl+K）或右下角的按鈕叫出 Studio AI：一句話查單字、記一筆交易、做一個 dashboard，它會直接帶你過去。';

function AppCard({ app }: { app: AppDef }) {
  return (
    <Card
      elevation={0}
      sx={{
        borderRadius: 3,
        border: '1px solid',
        borderColor: 'divider',
        background: (theme) =>
          `linear-gradient(135deg, ${alpha(app.color, 0.14)} 0%, ${alpha(
            theme.palette.background.paper,
            0.4,
          )} 60%)`,
        transition: 'transform 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease',
        '&:hover': {
          transform: 'translateY(-4px)',
          borderColor: alpha(app.color, 0.6),
          boxShadow: `0 12px 32px ${alpha(app.color, 0.25)}`,
        },
      }}
    >
      <CardActionArea
        component={Link}
        to={app.path}
        sx={{ p: 3, height: '100%', display: 'flex', alignItems: 'flex-start', gap: 2 }}
      >
        <Box
          sx={{
            flexShrink: 0,
            width: 52,
            height: 52,
            borderRadius: 2,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            bgcolor: alpha(app.color, 0.18),
            color: app.color,
            '& svg': { fontSize: 28 },
          }}
        >
          {cloneElement(app.icon, { fontSize: 'inherit' })}
        </Box>
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="h6" sx={{ fontWeight: 700, lineHeight: 1.25 }}>
              {app.name}
            </Typography>
            <ArrowForwardIcon sx={{ color: app.color, ml: 1, flexShrink: 0 }} />
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>
            {app.description}
          </Typography>
        </Box>
      </CardActionArea>
    </Card>
  );
}

function CatalogGrid({ apps }: { apps: AppDef[] }) {
  return (
    <Box
      sx={{
        display: 'grid',
        gap: 2.5,
        gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' },
      }}
    >
      {apps.map((app) => (
        <AppCard key={app.path} app={app} />
      ))}
    </Box>
  );
}

function GuestIntro() {
  return (
    <Box sx={{ textAlign: 'center', mb: { xs: 4, md: 5 } }}>
      <Typography variant="body1" sx={{ mb: 2 }}>
        AI 英文學習、投資組合追蹤與股票分析 — 登入後每天打開就知道今天該做什麼。字典免登入即可使用。
      </Typography>
      <Button
        variant="contained"
        href={`/api/account/login/Google?ReturnUrl=${encodeURIComponent('/')}`}
      >
        使用 Google 登入
      </Button>
    </Box>
  );
}

function Portal() {
  const { data: userInfoRes, isLoading } = useGetUserInfoQuery();
  // undefined while the first request is in flight, so neither the guest nor the today block flashes.
  const isLoggedIn = isLoading ? undefined : !!userInfoRes?.data;
  const displayName = userInfoRes?.data?.name || userInfoRes?.data?.email || '';

  return (
    <Box sx={{ maxWidth: 1000, mx: 'auto', py: { xs: 4, md: 8 }, px: 2 }}>
      {/* Hero */}
      <Box sx={{ textAlign: 'center', mb: { xs: 5, md: 7 } }}>
        <Typography
          variant="overline"
          sx={{ letterSpacing: 4, color: 'text.secondary', fontWeight: 600 }}
        >
          JACKY&apos;S STUDIO
        </Typography>
        <Typography variant="h3" sx={{ fontWeight: 800, mt: 1, mb: 1.5 }}>
          {displayName ? `歡迎回來，${displayName}` : '選擇一個應用程式'}
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ maxWidth: 560, mx: 'auto' }}>
          {isLoggedIn ? STUDIO_LINE : APPS_LINE}
        </Typography>
      </Box>

      {isLoggedIn === false && <GuestIntro />}
      {isLoggedIn && <BriefingCard />}
      {isLoggedIn && <TodaySection />}

      {/* Catalog */}
      <CatalogGrid apps={primaryApps} />
      <Typography
        variant="overline"
        component="h2"
        sx={{ display: 'block', mt: 5, mb: 1.5, letterSpacing: 2, color: 'text.secondary' }}
      >
        更多應用
      </Typography>
      <CatalogGrid apps={otherApps} />
    </Box>
  );
}

export default Portal;
