import { createSlice, PayloadAction } from '@reduxjs/toolkit';

export interface UiState {
  isCreditModalOpen: boolean;
  creditModalMessage?: string;
  requiredCredits?: number;
}

const initialState: UiState = {
  isCreditModalOpen: false,
};

export const uiSlice = createSlice({
  name: 'ui',
  initialState,
  reducers: {
    openCreditModal: (
      state,
      action: PayloadAction<{ message?: string; requiredCredits?: number } | undefined>,
    ) => {
      state.isCreditModalOpen = true;
      state.creditModalMessage = action.payload?.message;
      state.requiredCredits = action.payload?.requiredCredits;
    },
    closeCreditModal: (state) => {
      state.isCreditModalOpen = false;
      state.creditModalMessage = undefined;
      state.requiredCredits = undefined;
    },
  },
});

export const { openCreditModal, closeCreditModal } = uiSlice.actions;
export default uiSlice.reducer;
