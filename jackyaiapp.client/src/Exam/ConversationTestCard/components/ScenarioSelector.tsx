import {
  Box,
  Button,
  Card,
  CardContent,
  Typography,
  TextField,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Grid,
  Chip,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Alert,
} from '@mui/material';
import { useState } from 'react';

import { SCENARIO_TEMPLATES, ScenarioTemplate } from './scenarioTemplates';

import { CREDIT_COSTS } from '@/constants/creditCosts';

interface ScenarioSelectorProps {
  onSelectScenario: (
    scenario: string,
    userRole: string,
    aiRole: string,
    difficultyLevel: number,
  ) => void;
  disabled?: boolean;
}

function ScenarioSelector({ onSelectScenario, disabled }: ScenarioSelectorProps) {
  const [selectedTemplate, setSelectedTemplate] = useState<ScenarioTemplate | null>(null);
  const [difficultyLevel, setDifficultyLevel] = useState<number>(3);
  const [showCustomDialog, setShowCustomDialog] = useState(false);
  const [customScenario, setCustomScenario] = useState('');
  const [customUserRole, setCustomUserRole] = useState('');
  const [customAiRole, setCustomAiRole] = useState('');

  const handleTemplateSelect = (template: ScenarioTemplate) => {
    setSelectedTemplate(template);
  };

  const handleStartConversation = () => {
    if (selectedTemplate) {
      onSelectScenario(
        selectedTemplate.scenario,
        selectedTemplate.userRole,
        selectedTemplate.aiRole,
        difficultyLevel,
      );
    }
  };

  const handleCustomStart = () => {
    if (customScenario && customUserRole && customAiRole) {
      onSelectScenario(customScenario, customUserRole, customAiRole, difficultyLevel);
      setShowCustomDialog(false);
    }
  };

  const getDifficultyText = (level: number) => {
    switch (level) {
      case 1:
        return '初級 (簡單詞彙)';
      case 2:
        return '初中級 (基礎對話)';
      case 3:
        return '中級 (日常對話)';
      case 4:
        return '中高級 (複雜表達)';
      case 5:
        return '高級 (專業對話)';
      default:
        return '中級';
    }
  };

  return (
    <Box sx={{ maxWidth: 800, margin: 'auto', padding: 2 }}>
      <Typography
        variant="h5"
        gutterBottom
        sx={{
          textAlign: 'center',
        }}
      >
        選擇對話情境
      </Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mb: 3, textAlign: 'center' }}>
        選擇預設情境模板或自定義場景開始練習
      </Typography>
      <Grid container spacing={2} sx={{ mb: 3 }}>
        {SCENARIO_TEMPLATES.map((template) => (
          <Grid
            key={template.id}
            size={{
              xs: 12,
              sm: 6,
              md: 4,
            }}
          >
            <Card
              sx={{
                cursor: 'pointer',
                border: selectedTemplate?.id === template.id ? 2 : 1,
                borderColor: selectedTemplate?.id === template.id ? 'primary.main' : 'divider',
                '&:hover': {
                  borderColor: 'primary.main',
                  boxShadow: 2,
                },
              }}
              onClick={() => handleTemplateSelect(template)}
            >
              <CardContent>
                <Typography variant="h6" gutterBottom>
                  {template.name}
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                  {template.description}
                </Typography>
                <Typography
                  variant="caption"
                  sx={{
                    display: 'block',
                    mb: 1,
                  }}
                >
                  你的角色: {template.userRole}
                </Typography>
                <Typography
                  variant="caption"
                  sx={{
                    display: 'block',
                    mb: 1,
                  }}
                >
                  AI角色: {template.aiRole}
                </Typography>
                <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
                  {template.tags.map((tag) => (
                    <Chip key={tag} label={tag} size="small" variant="outlined" />
                  ))}
                </Box>
              </CardContent>
            </Card>
          </Grid>
        ))}
      </Grid>
      <Box sx={{ display: 'flex', gap: 2, justifyContent: 'center', mb: 3 }}>
        <Button variant="outlined" onClick={() => setShowCustomDialog(true)} disabled={disabled}>
          自定義情境
        </Button>
      </Box>
      {selectedTemplate && (
        <Box
          sx={{
            backgroundColor: 'background.paper',
            p: 2,
            borderRadius: 1,
            border: 1,
            borderColor: 'divider',
          }}
        >
          <Typography variant="h6" gutterBottom>
            已選擇: {selectedTemplate.name}
          </Typography>

          <FormControl fullWidth sx={{ mb: 2 }}>
            <InputLabel>難度等級</InputLabel>
            <Select
              value={difficultyLevel}
              onChange={(e) => setDifficultyLevel(Number(e.target.value))}
              label="難度等級"
            >
              {[1, 2, 3, 4, 5].map((level) => (
                <MenuItem key={level} value={level}>
                  {getDifficultyText(level)}
                </MenuItem>
              ))}
            </Select>
          </FormControl>

          <Alert severity="info" sx={{ mb: 2, py: 0.5 }}>
            開始對話消耗 <strong>{CREDIT_COSTS.CONVERSATION_START} credits</strong>， 之後每輪 AI
            回應再消耗 <strong>{CREDIT_COSTS.CONVERSATION_RESPONSE} credits</strong>。
          </Alert>
          <Button
            variant="contained"
            onClick={handleStartConversation}
            disabled={disabled}
            fullWidth
          >
            開始對話
          </Button>
        </Box>
      )}
      {/* 自定義情境對話框 */}
      <Dialog
        open={showCustomDialog}
        onClose={() => setShowCustomDialog(false)}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>自定義對話情境</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            margin="dense"
            label="情境描述"
            fullWidth
            multiline
            rows={3}
            value={customScenario}
            onChange={(e) => setCustomScenario(e.target.value)}
            placeholder="例如: A customer complaining about a defective product"
            sx={{ mb: 2 }}
          />
          <TextField
            margin="dense"
            label="你的角色"
            fullWidth
            value={customUserRole}
            onChange={(e) => setCustomUserRole(e.target.value)}
            placeholder="例如: Customer"
            sx={{ mb: 2 }}
          />
          <TextField
            margin="dense"
            label="AI角色"
            fullWidth
            value={customAiRole}
            onChange={(e) => setCustomAiRole(e.target.value)}
            placeholder="例如: Customer Service Representative"
            sx={{ mb: 2 }}
          />
          <FormControl fullWidth>
            <InputLabel>難度等級</InputLabel>
            <Select
              value={difficultyLevel}
              onChange={(e) => setDifficultyLevel(Number(e.target.value))}
              label="難度等級"
            >
              {[1, 2, 3, 4, 5].map((level) => (
                <MenuItem key={level} value={level}>
                  {getDifficultyText(level)}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setShowCustomDialog(false)}>取消</Button>
          <Button
            onClick={handleCustomStart}
            variant="contained"
            disabled={!customScenario || !customUserRole || !customAiRole}
          >
            開始對話
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

export default ScenarioSelector;
