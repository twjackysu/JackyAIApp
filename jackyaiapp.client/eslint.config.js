import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import importPlugin from 'eslint-plugin-import';
import prettierPlugin from 'eslint-plugin-prettier';
import prettierConfig from 'eslint-config-prettier';

export default tseslint.config(
  // Ignore patterns
  {
    ignores: ['dist/**', 'node_modules/**', '.eslintrc.cjs', '*.config.js', '*.config.ts'],
  },

  // Base configurations
  js.configs.recommended,
  ...tseslint.configs.recommended,
  prettierConfig,

  // Main configuration
  {
    files: ['**/*.{ts,tsx,js,jsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      sourceType: 'module',
      parser: tseslint.parser,
      parserOptions: {
        project: './tsconfig.json',
        ecmaFeatures: {
          jsx: true,
        },
      },
      globals: {
        browser: true,
        es2020: true,
      },
    },
    plugins: {
      '@typescript-eslint': tseslint.plugin,
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
      import: importPlugin,
      prettier: prettierPlugin,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      'prettier/prettier': 'error',
      'import/named': 'off',
      'import/namespace': 'off',
      'import/default': 'off',
      'import/export': 'off',
      'import/order': [
        'error',
        {
          groups: [
            'builtin', // Node.js 內建模組 (e.g., path, fs)
            'external', // 第三方模組 (e.g., react, lodash)
            'internal', // 本地專案內部模組 (透過路徑別名或 `~` 符號)
            'parent', // 父層級路徑 (../)
            'sibling', // 同層級路徑 (./)
            'index', // 索引檔案
            'object', // 解構導入
            'type', // 類型導入
          ],
          'newlines-between': 'always', // 各群組間留空行
          alphabetize: {
            order: 'asc', // 組內按字母升序排列
            caseInsensitive: true, // 不區分大小寫
          },
        },
      ],
    },
    settings: {
      'import/parsers': {
        '@typescript-eslint/parser': ['.ts', '.tsx'],
      },
      'import/resolver': {
        typescript: {
          alwaysTryTypes: true,
          project: './tsconfig.json',
        },
      },
    },
  },
);
