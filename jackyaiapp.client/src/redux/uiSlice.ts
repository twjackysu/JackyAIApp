import { createSlice } from '@reduxjs/toolkit';

export interface UiState {
  isUsageLimitModalOpen: boolean;
}

const initialState: UiState = {
  isUsageLimitModalOpen: false,
};

export const uiSlice = createSlice({
  name: 'ui',
  initialState,
  reducers: {
    openUsageLimitModal: (state) => {
      state.isUsageLimitModalOpen = true;
    },
    closeUsageLimitModal: (state) => {
      state.isUsageLimitModalOpen = false;
    },
  },
});

export const { openUsageLimitModal, closeUsageLimitModal } = uiSlice.actions;
export default uiSlice.reducer;
