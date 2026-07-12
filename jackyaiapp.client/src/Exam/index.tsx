import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { orange } from '@mui/material/colors';
import IconButton from '@mui/material/IconButton';
import { useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { Link, Route, Routes, useLocation, useNavigate } from 'react-router-dom';

import { useGetRepositoryWordsQuery } from '../apis/repositoryApis';
import { CREDIT_COSTS } from '../constants/creditCosts';

import ClozeTestCard from './ClozeTestCard';
import ConversationTestCard from './ConversationTestCard';
import ListeningTestCard from './ListeningTestCard';
import SentenceTestCard from './SentenceTestCard';
import TranslationTestCard from './TranslationTestCard';

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
  const isOnTestPage = location.pathname !== '/exam' && location.pathname !== '/exam/';

  return (
    <Box sx={{ p: 2, position: 'relative' }}>
      {isOnTestPage && (
        // Floating back button when on a specific test
        <IconButton
          onClick={() => navigate('/exam')}
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
          <Button component={Link} to="cloze" variant="outlined" sx={styles.button}>
            <Box sx={{ textAlign: 'center' }}>
              克漏字測驗 (Cloze Test)
              <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                每次生成 {CREDIT_COSTS.CLOZE_TEST} credits
              </Typography>
            </Box>
          </Button>
          <Button
            component={Link}
            to="translation"
            variant="outlined"
            color="secondary"
            sx={styles.button}
          >
            <Box sx={{ textAlign: 'center' }}>
              翻譯測驗 (Translation Test)
              <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                生成 {CREDIT_COSTS.TRANSLATION_TEST} credits・批改{' '}
                {CREDIT_COSTS.TRANSLATION_GRADING} credit
              </Typography>
            </Box>
          </Button>
          <Button
            component={Link}
            to="conversation"
            variant="outlined"
            color="success"
            sx={styles.button}
          >
            <Box sx={{ textAlign: 'center' }}>
              情境對話測驗 (Conversation Test)
              <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                開始 {CREDIT_COSTS.CONVERSATION_START} credits・每輪{' '}
                {CREDIT_COSTS.CONVERSATION_RESPONSE} credits
              </Typography>
            </Box>
          </Button>
          <Button
            component={Link}
            to="sentenceTest"
            variant="outlined"
            sx={{
              ...styles.button,
              color: orange[500],
              borderColor: orange[500],
              ':hover': { borderColor: orange[100] },
            }}
          >
            <Box sx={{ textAlign: 'center' }}>
              造句測驗 (Sentence Formation Test)
              <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                每次生成 {CREDIT_COSTS.SENTENCE_TEST} credits
              </Typography>
            </Box>
          </Button>
          <Button
            component={Link}
            to="listening"
            variant="outlined"
            color="info"
            sx={styles.button}
          >
            <Box sx={{ textAlign: 'center' }}>
              聽力測驗 (Listening Test)
              <Typography variant="caption" sx={{ display: 'block', opacity: 0.75 }}>
                每次生成 {CREDIT_COSTS.LISTENING_TEST} credits
              </Typography>
            </Box>
          </Button>
        </Box>
      )}
      {isRepositoryNoWord && !isOnTestPage && (
        <Alert severity="info" sx={{ maxWidth: 800, mx: 'auto', mb: 3 }}>
          你的儲存庫還沒有單字，測驗會從整本字典隨機出題。到字典搜尋單字並加入收藏後，測驗會優先針對你的單字出題。
        </Alert>
      )}
      <Routes>
        <Route path="cloze" element={<ClozeTestCard />} />
        <Route path="translation" element={<TranslationTestCard />} />
        <Route path="conversation" element={<ConversationTestCard />} />
        <Route path="sentenceTest" element={<SentenceTestCard />} />
        <Route path="listening" element={<ListeningTestCard />} />
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
