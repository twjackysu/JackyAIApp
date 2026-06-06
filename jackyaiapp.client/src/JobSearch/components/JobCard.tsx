import AccessTimeIcon from '@mui/icons-material/AccessTime';
import BusinessIcon from '@mui/icons-material/Business';
import LocationOnIcon from '@mui/icons-material/LocationOn';
import MonetizationOnIcon from '@mui/icons-material/MonetizationOn';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import { JobListing } from '@/apis/jobApis/types';

interface JobCardProps {
  job: JobListing;
}

function formatRelativeTime(isoDate: string): string {
  const now = Date.now();
  const then = new Date(isoDate).getTime();
  const diffMs = now - then;
  const diffMins = Math.floor(diffMs / 60_000);
  const diffHrs = Math.floor(diffMs / 3_600_000);
  const diffDays = Math.floor(diffMs / 86_400_000);

  if (diffMins < 60) return `${diffMins} 分鐘前`;
  if (diffHrs < 24) return `${diffHrs} 小時前`;
  if (diffDays < 7) return `${diffDays} 天前`;
  return new Date(isoDate).toLocaleDateString('zh-TW', { month: 'short', day: 'numeric' });
}

function platformColor(platform: string): 'primary' | 'secondary' | 'success' | 'info' | 'warning' {
  if (platform.includes('104')) return 'primary';
  return 'secondary';
}

export default function JobCard({ job }: JobCardProps) {
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        transition: 'box-shadow 0.2s',
        '&:hover': { boxShadow: 3 },
      }}
    >
      {/* Header row: title + open link */}
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={1}>
        <Typography variant="subtitle1" fontWeight="bold" sx={{ lineHeight: 1.3 }}>
          {job.title || '（未提供職位名稱）'}
        </Typography>
        <Tooltip title="在新分頁開啟">
          <IconButton
            size="small"
            href={job.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="open job listing"
            sx={{ mt: -0.5, flexShrink: 0 }}
          >
            <OpenInNewIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>

      {/* Meta row */}
      <Stack direction="row" flexWrap="wrap" gap={1.5} mt={0.75} mb={1}>
        {job.company && (
          <Stack direction="row" alignItems="center" spacing={0.5}>
            <BusinessIcon sx={{ fontSize: 14, color: 'text.secondary' }} />
            <Typography variant="body2" color="text.secondary">
              {job.company}
            </Typography>
          </Stack>
        )}
        {job.location && (
          <Stack direction="row" alignItems="center" spacing={0.5}>
            <LocationOnIcon sx={{ fontSize: 14, color: 'text.secondary' }} />
            <Typography variant="body2" color="text.secondary">
              {job.location}
            </Typography>
          </Stack>
        )}
        {job.salaryRange && (
          <Stack direction="row" alignItems="center" spacing={0.5}>
            <MonetizationOnIcon sx={{ fontSize: 14, color: 'success.main' }} />
            <Typography variant="body2" color="success.main" fontWeight="medium">
              {job.salaryRange}
            </Typography>
          </Stack>
        )}
      </Stack>

      {/* Description snippet */}
      {job.description && (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{
            overflow: 'hidden',
            display: '-webkit-box',
            WebkitBoxOrient: 'vertical',
            WebkitLineClamp: 2,
            mb: 1.5,
          }}
        >
          {job.description}
        </Typography>
      )}

      {/* Footer: platform + tags + time */}
      <Stack
        direction="row"
        justifyContent="space-between"
        alignItems="center"
        flexWrap="wrap"
        gap={1}
      >
        <Stack direction="row" flexWrap="wrap" gap={0.5}>
          <Chip
            label={job.platform}
            size="small"
            color={job.platform.includes('Cake') ? 'default' : platformColor(job.platform)}
            variant="outlined"
            sx={{
              height: 20,
              fontSize: '0.7rem',
              // Cake brand teal as a subtle outline (palette has no teal)
              ...(job.platform.includes('Cake') && { borderColor: '#00b3a4', color: '#00897b' }),
            }}
          />
          {job.tags.slice(0, 4).map((tag) => (
            <Chip
              key={tag}
              label={tag}
              size="small"
              variant="outlined"
              sx={{ height: 20, fontSize: '0.7rem' }}
            />
          ))}
        </Stack>
        <Stack direction="row" alignItems="center" spacing={0.5}>
          <AccessTimeIcon sx={{ fontSize: 12, color: 'text.disabled' }} />
          <Typography variant="caption" color="text.disabled">
            {formatRelativeTime(job.postedAt)}
          </Typography>
        </Stack>
      </Stack>
    </Paper>
  );
}
