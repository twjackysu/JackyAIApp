import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import { orange } from '@mui/material/colors';
import IconButton from '@mui/material/IconButton';
import { alpha, useTheme } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { Link, Route, Routes, useLocation, useNavigate } from 'react-router-dom';

import { useGetRepositoryWordsQuery } from '../apis/repositoryApis';
import RepositoryNoWordAlert from '../components/RepositoryNoWordAlert';

import ClozeTestCard from './ClozeTestCard';
import ConversationTestCard from './ConversationTestCard';
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
  const isRepositoryNoWord = !data?.data?.[0];

  // Check if we're on a specific test page
  const isOnTestPage = location.pathname !== '/exam' && location.pathname !== '/exam/';

  // Check if we're on conversation test page
  const isOnConversationTest = location.pathname.includes('/conversation');

  return (
    <Box sx={{ p: 2, position: 'relative' }}>
      {isRepositoryNoWord && !isOnConversationTest ? (
        <RepositoryNoWordAlert />
      ) : (
        <>
          {isOnTestPage && (
            // Floating back button when on a specific test
            <IconButton
              onClick={() => navigate('/exam')}
              sx={{
                position: 'absolute',
                top: 16,
                left: 16,
                zIndex: 10,
                backgroundColor: alpha(theme.palette.background.paper, 0.9),
                boxShadow: 2,
                '&:hover': {
                  backgroundColor: alpha(theme.palette.background.paper, 1),
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
              <Button
                component={Link}
                to="cloze"
                variant="outlined"
                sx={styles.button}
                disabled={isRepositoryNoWord}
              >
                克漏字測驗 (Cloze Test)
              </Button>
              <Button
                component={Link}
                to="translation"
                variant="outlined"
                color="secondary"
                sx={styles.button}
                disabled={isRepositoryNoWord}
              >
                翻譯測驗 (Translation Test)
              </Button>
              <Button
                component={Link}
                to="conversation"
                variant="outlined"
                color="success"
                sx={styles.button}
              >
                情境對話測驗 (Conversation Test)
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
                disabled={isRepositoryNoWord}
              >
                造句測驗 (Sentence Formation Test)
              </Button>
            </Box>
          )}
          {isRepositoryNoWord && !isOnConversationTest && <RepositoryNoWordAlert />}
          <Routes>
            <Route
              path="cloze"
              element={isRepositoryNoWord ? <RepositoryNoWordAlert /> : <ClozeTestCard />}
            />
            <Route
              path="translation"
              element={isRepositoryNoWord ? <RepositoryNoWordAlert /> : <TranslationTestCard />}
            />
            <Route path="conversation" element={<ConversationTestCard />} />
            <Route
              path="sentenceTest"
              element={isRepositoryNoWord ? <RepositoryNoWordAlert /> : <SentenceTestCard />}
            />
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
        </>
      )}
    </Box>
  );
}

export default Exam;
