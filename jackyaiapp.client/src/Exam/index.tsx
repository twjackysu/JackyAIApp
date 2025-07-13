import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import { orange } from '@mui/material/colors';
import { Link, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { useGetRepositoryWordsQuery } from '../apis/repositoryApis';
import RepositoryNoWordAlert from '../components/RepositoryNoWordAlert';
import ClozeTestCard from './ClozeTestCard';
import ConversationTestCard from './ConversationTestCard';
import TranslationTestCard from './TranslationTestCard';
import { alpha, useTheme } from '@mui/material/styles';

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
  
  // Get current test name for back button
  const getTestName = () => {
    const path = location.pathname.split('/').pop();
    switch (path) {
      case 'cloze':
        return '克漏字測驗';
      case 'translation':
        return '翻譯測驗';
      case 'conversation':
        return '情境對話測驗';
      case 'sentenceTest':
        return '造句測驗';
      default:
        return '測驗';
    }
  };
  
  return (
    <Box sx={{ p: 2, position: 'relative' }}>
      {isRepositoryNoWord ? (
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
                }
              }}
            >
              <ArrowBackIcon />
            </IconButton>
          )}
          {!isOnTestPage && (
            // Test selection buttons when on main exam page
            <Box
              sx={{
                position: 'relative',
                mb: 3,
                '&::before': {
                  content: '""',
                  position: 'absolute',
                  top: 0,
                  right: 0,
                  bottom: 0,
                  width: '20px',
                  background: `linear-gradient(to left, ${theme.palette.background.default}, transparent)`,
                  zIndex: 1,
                  pointerEvents: 'none',
                },
                '&::after': {
                  content: '""',
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  bottom: 0,
                  width: '20px',
                  background: `linear-gradient(to right, ${theme.palette.background.default}, transparent)`,
                  zIndex: 1,
                  pointerEvents: 'none',
                },
              }}
            >
              <Box
                sx={{
                  display: 'flex',
                  gap: 2,
                  overflowX: 'auto',
                  pb: 1,
                  px: 2,
                  scrollbarWidth: 'thin',
                  '&::-webkit-scrollbar': {
                    height: '6px',
                  },
                  '&::-webkit-scrollbar-track': {
                    backgroundColor: alpha(theme.palette.action.hover, 0.1),
                    borderRadius: '3px',
                  },
                  '&::-webkit-scrollbar-thumb': {
                    backgroundColor: alpha(theme.palette.action.active, 0.3),
                    borderRadius: '3px',
                    '&:hover': {
                      backgroundColor: alpha(theme.palette.action.active, 0.5),
                    },
                  },
                }}
              >
                <Button component={Link} to="cloze" variant="outlined" sx={styles.button}>
                  克漏字測驗 (Cloze Test)
                </Button>
                <Button
                  component={Link}
                  to="translation"
                  variant="outlined"
                  color="secondary"
                  sx={styles.button}
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
                >
                  造句測驗 (Sentence Formation Test)
                </Button>
              </Box>
            </Box>
          )}
          <Routes>
            <Route path="cloze" element={<ClozeTestCard />} />
            <Route path="translation" element={<TranslationTestCard />} />
            <Route path="conversation" element={<ConversationTestCard />} />
            <Route path="sentenceTest" element={<div>造句測驗還沒做，請選擇其他類型...</div>} />
            <Route path="/" element={
              <Box sx={{ 
                textAlign: 'center', 
                mt: 4,
                color: 'text.secondary'
              }}>
                <Typography variant="h6" gutterBottom>
                  歡迎使用英語學習測驗系統 📚
                </Typography>
                <Typography variant="body1">
                  請從上方選擇一種測驗類型開始練習
                </Typography>
              </Box>
            } />
          </Routes>
        </>
      )}
    </Box>
  );
}

export default Exam;
