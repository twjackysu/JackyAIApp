import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button, { ButtonProps } from '@mui/material/Button';
import { orange } from '@mui/material/colors';
import IconButton from '@mui/material/IconButton';
import { SxProps, Theme, useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { ReactNode } from 'react';
import { Link, Route, Routes, useLocation, useNavigate } from 'react-router-dom';

import { useGetRepositoryWordsQuery } from '../apis/repositoryApis';
import { CREDIT_COSTS } from '../constants/creditCosts';

import ClozeTestCard from './ClozeTestCard';
import ConversationTestCard from './ConversationTestCard';
import DictationTestCard from './DictationTestCard';
import ListeningResponseTestCard from './ListeningResponseTestCard';
import ListeningTestCard from './ListeningTestCard';
import ReadingTestCard from './ReadingTestCard';
import SentenceTestCard from './SentenceTestCard';
import TranslationTestCard from './TranslationTestCard';
import WritingTestCard from './WritingTestCard';

const styles = {
  button: {
    px: 4,
    py: 2,
    fontSize: '1rem',
    textTransform: 'none',
    boxShadow: 2,
    minWidth: 'max-content',
    whiteSpace: 'nowrap',
  },
};

interface ExamMode {
  /** Route path under /english/exam, also used as the React key. */
  path: string;
  label: string;
  /** Credit-cost caption. A node rather than a number because the tests that charge twice
   * (generate + grade, start + per turn) spell out both costs. */
  cost: ReactNode;
  color?: ButtonProps['color'];
  /** Only for a mode whose colour isn't in the MUI palette; replaces `styles.button`
   * wholesale, so it has to carry the shared button styling itself. */
  sx?: SxProps<Theme>;
  element: ReactNode;
}

/** The single source of truth for the exam menu: one entry drives both the selection button
 * and the route below, so adding a test type no longer means editing the same path twice. */
const EXAM_MODES: ExamMode[] = [
  {
    path: 'cloze',
    label: '克漏字測驗 (Cloze Test)',
    cost: <>每次生成 {CREDIT_COSTS.CLOZE_TEST} 點使用額度</>,
    element: <ClozeTestCard />,
  },
  {
    path: 'translation',
    label: '翻譯測驗 (Translation Test)',
    cost: (
      <>
        生成 {CREDIT_COSTS.TRANSLATION_TEST} 點使用額度・批改 {CREDIT_COSTS.TRANSLATION_GRADING}{' '}
        點使用額度
      </>
    ),
    color: 'secondary',
    element: <TranslationTestCard />,
  },
  {
    path: 'conversation',
    label: '情境對話測驗 (Conversation Test)',
    cost: (
      <>
        開始 {CREDIT_COSTS.CONVERSATION_START} 點使用額度・每輪 {CREDIT_COSTS.CONVERSATION_RESPONSE}{' '}
        點使用額度
      </>
    ),
    color: 'success',
    element: <ConversationTestCard />,
  },
  {
    path: 'sentenceTest',
    label: '造句測驗 (Sentence Formation Test)',
    cost: <>每次生成 {CREDIT_COSTS.SENTENCE_TEST} 點使用額度</>,
    sx: {
      ...styles.button,
      color: orange[500],
      borderColor: orange[500],
      ':hover': { borderColor: orange[100] },
    },
    element: <SentenceTestCard />,
  },
  {
    path: 'listening',
    label: '聽力測驗 - 看圖 (Listening Part 1)',
    cost: <>每次生成 {CREDIT_COSTS.LISTENING_TEST} 點使用額度</>,
    color: 'info',
    element: <ListeningTestCard />,
  },
  {
    path: 'listeningResponse',
    label: '聽力測驗 - 應答 (Listening Part 2)',
    cost: <>每次生成 {CREDIT_COSTS.LISTENING_RESPONSE_TEST} 點使用額度</>,
    color: 'info',
    element: <ListeningResponseTestCard />,
  },
  {
    path: 'reading',
    label: '閱讀理解測驗 (Reading Comprehension)',
    cost: <>每次生成 {CREDIT_COSTS.READING_COMPREHENSION_TEST} 點使用額度</>,
    color: 'warning',
    element: <ReadingTestCard />,
  },
  {
    path: 'writing',
    label: '寫作練習 (Writing Practice)',
    // Free to open — only submitting for AI批改 charges credits, unlike every other mode.
    cost: <>提交批改需消耗 {CREDIT_COSTS.WRITING_EVALUATION} 點使用額度</>,
    color: 'secondary',
    element: <WritingTestCard />,
  },
  {
    path: 'dictation',
    label: '聽寫練習 (Dictation)',
    cost: <>每次生成 {CREDIT_COSTS.DICTATION_TEST} 點使用額度</>,
    color: 'info',
    element: <DictationTestCard />,
  },
];

function Exam() {
  const theme = useTheme();
  const location = useLocation();
  const navigate = useNavigate();
  const { data } = useGetRepositoryWordsQuery({
    pageNumber: 1,
    pageSize: 1,
  });
  // Word-based tests fall back to a random dictionary word when the user's
  // repository is empty (backend handles this), so this is informational only —
  // it no longer blocks access to any test.
  const isRepositoryNoWord = !data?.data?.[0];

  // Check if we're on a specific test page
  const isOnTestPage =
    location.pathname !== '/english/exam' && location.pathname !== '/english/exam/';

  return (
    <Box sx={{ p: 2, position: 'relative' }}>
      {isOnTestPage && (
        // Floating back button when on a specific test
        <IconButton
          onClick={() => navigate('/english/exam')}
          sx={{
            position: 'absolute',
            top: 16,
            left: 16,
            zIndex: 10,
            backgroundColor: theme.alpha(theme.palette.background.paper, 0.9),
            boxShadow: 2,
            '&:hover': {
              backgroundColor: theme.alpha(theme.palette.background.paper, 1),
              boxShadow: 4,
            },
          }}
        >
          <ArrowBackIcon />
        </IconButton>
      )}
      {!isOnTestPage && (
        // Test selection buttons when on main exam page
        <Box
          sx={{
            mb: 3,
            display: 'flex',
            flexWrap: 'wrap',
            gap: 2,
            justifyContent: 'center',
            px: 2,
          }}
        >
          {EXAM_MODES.map((mode) => (
            <Button
              key={mode.path}
              component={Link}
              to={mode.path}
              variant="outlined"
              color={mode.color}
              sx={mode.sx ?? styles.button}
            >
              <Box sx={{ textAlign: 'center' }}>
                {mode.label}
                <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                  {mode.cost}
                </Typography>
              </Box>
            </Button>
          ))}
        </Box>
      )}
      {isRepositoryNoWord && !isOnTestPage && (
        <Alert severity="info" sx={{ maxWidth: 800, mx: 'auto', mb: 3 }}>
          你的儲存庫還沒有單字，測驗會從整本字典隨機出題。到字典搜尋單字並加入收藏後，測驗會優先針對你的單字出題。
        </Alert>
      )}
      <Routes>
        {EXAM_MODES.map((mode) => (
          <Route key={mode.path} path={mode.path} element={mode.element} />
        ))}
        <Route
          path="/"
          element={
            <Box
              sx={{
                textAlign: 'center',
                mt: 4,
                color: 'text.secondary',
              }}
            >
              <Typography variant="h6" gutterBottom>
                歡迎使用英語學習測驗系統 📚
              </Typography>
              <Typography variant="body1">請從上方選擇一種測驗類型開始練習</Typography>
            </Box>
          }
        />
      </Routes>
    </Box>
  );
}

export default Exam;
