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
} from '@mui/material';
import { useState } from 'react';

interface ScenarioTemplate {
  id: string;
  name: string;
  scenario: string;
  userRole: string;
  aiRole: string;
  description: string;
  tags: string[];
}

interface ScenarioSelectorProps {
  onSelectScenario: (
    scenario: string,
    userRole: string,
    aiRole: string,
    difficultyLevel: number,
  ) => void;
  disabled?: boolean;
}

// 預設情境模板
const SCENARIO_TEMPLATES: ScenarioTemplate[] = [
  {
    id: 'restaurant',
    name: '餐廳點餐',
    scenario: 'A customer ordering food at a restaurant',
    userRole: 'Customer',
    aiRole: 'Waiter/Waitress',
    description: '練習在餐廳點餐的日常對話',
    tags: ['日常', '食物', '服務'],
  },
  {
    id: 'hotel',
    name: '酒店入住',
    scenario: 'A guest checking into a hotel',
    userRole: 'Guest',
    aiRole: 'Hotel Receptionist',
    description: '學習酒店入住登記相關英語',
    tags: ['旅遊', '住宿', '服務'],
  },
  {
    id: 'shopping',
    name: '購物詢問',
    scenario: 'A customer asking about products in a store',
    userRole: 'Customer',
    aiRole: 'Shop Assistant',
    description: '練習購物時的詢問和討論',
    tags: ['購物', '商品', '價格'],
  },
  {
    id: 'interview',
    name: '工作面試',
    scenario: 'A job interview conversation',
    userRole: 'Job Candidate',
    aiRole: 'Interviewer',
    description: '模擬工作面試情境',
    tags: ['工作', '面試', '專業'],
  },
  {
    id: 'doctor',
    name: '看醫生',
    scenario: 'A patient visiting a doctor',
    userRole: 'Patient',
    aiRole: 'Doctor',
    description: '學習看病時的醫療英語',
    tags: ['醫療', '健康', '症狀'],
  },
  {
    id: 'airport',
    name: '機場問路',
    scenario: 'A traveler asking for directions at an airport',
    userRole: 'Traveler',
    aiRole: 'Airport Staff',
    description: '練習在機場問路和查詢資訊',
    tags: ['旅遊', '交通', '方向'],
  },
];

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
      <Typography variant="h5" gutterBottom textAlign="center">
        選擇對話情境
      </Typography>

      <Typography variant="body1" color="text.secondary" sx={{ mb: 3, textAlign: 'center' }}>
        選擇預設情境模板或自定義場景開始練習
      </Typography>

      <Grid container spacing={2} sx={{ mb: 3 }}>
        {SCENARIO_TEMPLATES.map((template) => (
          <Grid item xs={12} sm={6} md={4} key={template.id}>
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
                <Typography variant="caption" display="block" sx={{ mb: 1 }}>
                  你的角色: {template.userRole}
                </Typography>
                <Typography variant="caption" display="block" sx={{ mb: 1 }}>
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
