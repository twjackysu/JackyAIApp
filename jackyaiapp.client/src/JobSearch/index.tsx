import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import WorkIcon from '@mui/icons-material/Work';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState, useCallback } from 'react';

import JobCard from './components/JobCard';

import { useGetListingsQuery } from '@/apis/jobApis';
import { JobSearchParams } from '@/apis/jobApis/types';
import { ActionTooltip } from '@/components/hints';

const ALL_PLATFORMS = [
  { id: '104', label: '104 人力銀行' },
  { id: 'cake', label: 'Cake' },
];

const DEFAULT_KEYWORDS = 'senior software engineer';

function JobSearch() {
  const [inputKeywords, setInputKeywords] = useState(DEFAULT_KEYWORDS);
  const [searchParams, setSearchParams] = useState<JobSearchParams>({
    keywords: DEFAULT_KEYWORDS,
    strategy: 'AND',
    platforms: ALL_PLATFORMS.map((p) => p.id),
  });

  const { data, isFetching, isError, error, refetch } = useGetListingsQuery(searchParams, {
    refetchOnMountOrArgChange: true,
  });

  const jobs = data?.data ?? [];

  const handleSearch = useCallback(() => {
    if (!inputKeywords.trim()) return;
    const trimmed = inputKeywords.trim();
    if (trimmed === searchParams.keywords) {
      refetch();
    } else {
      setSearchParams((prev) => ({ ...prev, keywords: trimmed }));
    }
  }, [inputKeywords, searchParams.keywords, refetch]);

  const handleRefresh = useCallback(() => {
    refetch();
  }, [refetch]);

  const togglePlatform = useCallback((id: string) => {
    setSearchParams((prev) => {
      const has = prev.platforms.includes(id);
      const next = has ? prev.platforms.filter((p) => p !== id) : [...prev.platforms, id];
      return next.length > 0 ? { ...prev, platforms: next } : prev;
    });
  }, []);

  const toggleStrategy = useCallback((strategy: 'AND' | 'OR') => {
    setSearchParams((prev) => ({ ...prev, strategy }));
  }, []);

  return (
    <Box sx={{ p: { xs: 1.5, sm: 3 }, maxWidth: 900, mx: 'auto' }}>
      {/* Header */}
      <Stack
        direction="row"
        spacing={1}
        sx={{
          alignItems: 'center',
          mb: 3,
        }}
      >
        <WorkIcon color="primary" />
        <Typography
          variant="h5"
          sx={{
            fontWeight: 'bold',
          }}
        >
          職缺搜尋
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ ml: 1 }}>
          台灣 · 按發布時間排序
        </Typography>
      </Stack>
      {/* Search controls */}
      <Box
        sx={{
          p: 2,
          border: '1px solid',
          borderColor: 'divider',
          borderRadius: 1,
          mb: 3,
        }}
      >
        {/* Keyword input + search */}
        <Stack
          direction="row"
          spacing={1}
          sx={{
            mb: 2,
          }}
        >
          <TextField
            fullWidth
            size="small"
            placeholder="e.g. senior engineer .NET react"
            value={inputKeywords}
            onChange={(e) => setInputKeywords(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            slotProps={{
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon fontSize="small" />
                  </InputAdornment>
                ),
              },
            }}
          />
          <Button
            variant="contained"
            onClick={handleSearch}
            disabled={isFetching || !inputKeywords.trim()}
            sx={{ minWidth: 80 }}
          >
            {isFetching ? <CircularProgress size={18} color="inherit" /> : '搜尋'}
          </Button>
          <ActionTooltip title="重新整理（清除快取）">
            <span>
              <IconButton onClick={handleRefresh} disabled={isFetching}>
                <RefreshIcon />
              </IconButton>
            </span>
          </ActionTooltip>
        </Stack>

        {/* Strategy toggle + platform chips */}
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          spacing={2}
          sx={{
            alignItems: { sm: 'center' },
          }}
        >
          {/* AND / OR */}
          <Stack
            direction="row"
            spacing={1}
            sx={{
              alignItems: 'center',
              flexShrink: 0,
            }}
          >
            <Typography variant="body2" color="text.secondary">
              策略
            </Typography>
            <ButtonGroup size="small">
              <Button
                variant={searchParams.strategy === 'AND' ? 'contained' : 'outlined'}
                onClick={() => toggleStrategy('AND')}
              >
                AND
              </Button>
              <Button
                variant={searchParams.strategy === 'OR' ? 'contained' : 'outlined'}
                onClick={() => toggleStrategy('OR')}
              >
                OR
              </Button>
            </ButtonGroup>
            <Typography variant="caption" color="text.secondary">
              {searchParams.strategy === 'AND' ? '全部關鍵字都要符合' : '任一關鍵字符合'}
            </Typography>
          </Stack>

          <Divider orientation="vertical" flexItem sx={{ display: { xs: 'none', sm: 'block' } }} />

          {/* Platform chips */}
          <Stack
            direction="row"
            sx={{
              gap: 0.75,
              flexWrap: 'wrap',
              alignItems: 'center',
            }}
          >
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{
                flexShrink: 0,
              }}
            >
              來源
            </Typography>
            {ALL_PLATFORMS.map((p) => {
              const selected = searchParams.platforms.includes(p.id);
              return (
                <Chip
                  key={p.id}
                  label={p.label}
                  size="small"
                  onClick={() => togglePlatform(p.id)}
                  color={selected ? 'primary' : 'default'}
                  variant={selected ? 'filled' : 'outlined'}
                  clickable
                />
              );
            })}
          </Stack>
        </Stack>
      </Box>
      {/* Results */}
      {isError && (
        <Alert severity="error" sx={{ mb: 2 }}>
          搜尋失敗：
          {error && 'status' in error ? ` HTTP ${error.status}` : '請稍後再試'}
        </Alert>
      )}
      {isFetching && (
        <Stack
          spacing={2}
          sx={{
            py: 6,
            alignItems: 'center',
          }}
        >
          <CircularProgress />
          <Typography variant="body2" color="text.secondary">
            正在從各平台抓取最新職缺...
          </Typography>
        </Stack>
      )}
      {!isFetching && !isError && (
        <>
          <Stack
            direction="row"
            sx={{
              justifyContent: 'space-between',
              alignItems: 'center',
              mb: 1.5,
            }}
          >
            <Typography variant="body2" color="text.secondary">
              共 {jobs.length} 筆結果
              {jobs.length > 0 && (
                <> · 最新：{new Date(jobs[0].postedAt).toLocaleDateString('zh-TW')}</>
              )}
            </Typography>
            {jobs.length > 0 && (
              <Typography variant="caption" color="text.disabled">
                快取 30 分鐘，按 🔄 強制更新
              </Typography>
            )}
          </Stack>

          {jobs.length === 0 ? (
            <Alert severity="info">
              沒有找到符合「{searchParams.keywords}」的職缺。 試試 OR 策略，或調整關鍵字。
            </Alert>
          ) : (
            <Stack spacing={1.5}>
              {jobs.map((job) => (
                <JobCard key={job.id} job={job} />
              ))}
            </Stack>
          )}
        </>
      )}
    </Box>
  );
}

export default JobSearch;
