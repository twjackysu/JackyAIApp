import SettingsIcon from '@mui/icons-material/Settings';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Checkbox from '@mui/material/Checkbox';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormGroup from '@mui/material/FormGroup';
import Grid from '@mui/material/Grid';
import Slider from '@mui/material/Slider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

export interface AnalysisConfig {
  includeTechnical: boolean;
  includeChip: boolean;
  includeFundamental: boolean;
  includeScoring: boolean;
  includeRisk: boolean;
  technicalWeight: number;
  chipWeight: number;
  fundamentalWeight: number;
}

interface Props {
  config: AnalysisConfig;
  onChange: (c: AnalysisConfig) => void;
}

export const AnalysisConfigPanel = ({ config, onChange }: Props) => {
  const toggle = (f: keyof AnalysisConfig) => onChange({ ...config, [f]: !config[f] });
  const slide = (f: 'technicalWeight' | 'chipWeight' | 'fundamentalWeight') =>
    (_: Event, v: number | number[]) => onChange({ ...config, [f]: (v as number) / 100 });

  return (
    <Accordion>
      <AccordionSummary expandIcon={<SettingsIcon />}>
        <Typography variant="subtitle1" fontWeight="bold">⚙️ 分析設定</Typography>
      </AccordionSummary>
      <AccordionDetails>
        <Grid container spacing={3}>
          <Grid item xs={12} md={6}>
            <Typography variant="subtitle2" gutterBottom fontWeight="bold">分析項目</Typography>
            <FormGroup>
              <FormControlLabel control={<Checkbox checked={config.includeTechnical} onChange={() => toggle('includeTechnical')} />} label="📊 技術面指標" />
              <FormControlLabel control={<Checkbox checked={config.includeChip} onChange={() => toggle('includeChip')} />} label="🏦 籌碼面指標" />
              <FormControlLabel control={<Checkbox checked={config.includeFundamental} onChange={() => toggle('includeFundamental')} />} label="📋 基本面指標" />
              <FormControlLabel control={<Checkbox checked={config.includeScoring} onChange={() => toggle('includeScoring')} />} label="🎯 綜合評分" />
              <FormControlLabel control={<Checkbox checked={config.includeRisk} onChange={() => toggle('includeRisk')} />} label="⚠️ 風險評估" />
            </FormGroup>
          </Grid>
          <Grid item xs={12} md={6}>
            <Typography variant="subtitle2" gutterBottom fontWeight="bold">類別權重</Typography>
            <Stack spacing={2}>
              {(['technicalWeight', 'chipWeight', 'fundamentalWeight'] as const).map((f) => {
                const labels = { technicalWeight: '📊 技術面', chipWeight: '🏦 籌碼面', fundamentalWeight: '📋 基本面' };
                const disabledMap = { technicalWeight: !config.includeTechnical, chipWeight: !config.includeChip, fundamentalWeight: !config.includeFundamental };
                return (
                  <Box key={f}>
                    <Typography variant="body2" gutterBottom>{labels[f]}: {(config[f] * 100).toFixed(0)}%</Typography>
                    <Slider value={config[f] * 100} onChange={slide(f)} min={0} max={100} step={5} disabled={disabledMap[f]} valueLabelDisplay="auto" valueLabelFormat={(v) => `${v}%`} />
                  </Box>
                );
              })}
            </Stack>
            <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>* 權重會自動正規化</Typography>
          </Grid>
        </Grid>
      </AccordionDetails>
    </Accordion>
  );
};

export default AnalysisConfigPanel;
