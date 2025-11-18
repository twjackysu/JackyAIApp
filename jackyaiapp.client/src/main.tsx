import Container from '@mui/material/Container';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider, createTheme } from '@mui/material/styles';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import { BrowserRouter } from 'react-router-dom';

import { store } from '@/redux/store';

import App from './App';
import 'highlight.js/styles/github-dark.css';

const darkTheme = createTheme({
  palette: {
    mode: 'dark',
  },
  components: {
    MuiButton: {
      styleOverrides: {
        root: {
          textTransform: 'none', // forbid uppercase
        },
      },
    },
    MuiCssBaseline: {
      styleOverrides: {
        '*': {
          // 自定義 scrollbar 樣式
          '&::-webkit-scrollbar': {
            width: '8px',
            height: '8px',
          },
          '&::-webkit-scrollbar-track': {
            backgroundColor: 'rgba(255, 255, 255, 0.05)',
            borderRadius: '4px',
          },
          '&::-webkit-scrollbar-thumb': {
            backgroundColor: 'rgba(255, 255, 255, 0.2)',
            borderRadius: '4px',
            '&:hover': {
              backgroundColor: 'rgba(255, 255, 255, 0.3)',
            },
            '&:active': {
              backgroundColor: 'rgba(255, 255, 255, 0.4)',
            },
          },
          '&::-webkit-scrollbar-corner': {
            backgroundColor: 'transparent',
          },
        },
        // Firefox scrollbar
        html: {
          scrollbarWidth: 'thin',
          scrollbarColor: 'rgba(255, 255, 255, 0.2) rgba(255, 255, 255, 0.05)',
        },
      },
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Provider store={store}>
      <ThemeProvider theme={darkTheme}>
        <CssBaseline />
        <Container maxWidth="lg">
          <BrowserRouter>
            <App />
          </BrowserRouter>
        </Container>
      </ThemeProvider>
    </Provider>
  </StrictMode>,
);
