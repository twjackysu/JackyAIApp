import { lengthProblem } from './holdingsLimits';

import { AssetType, PositionInput } from '@/apis/portfolioApis/types';

export interface EditorRow extends PositionInput {
  _key: string;
}

let counter = 0;
const nextKey = () => `row_${Date.now()}_${counter++}`;

export const makeRow = (assetType: AssetType, market = 'TW'): EditorRow => {
  switch (assetType) {
    case 'STOCK':
      return {
        _key: nextKey(),
        assetType: 'STOCK',
        symbol: '',
        name: '',
        market,
        currency: market === 'US' ? 'USD' : 'TWD',
        // Stocks are paid in their market currency.
        costCurrency: market === 'US' ? 'USD' : 'TWD',
        quantity: 0,
        costBasis: null,
      };
    case 'CASH':
      return {
        _key: nextKey(),
        assetType: 'CASH',
        symbol: '',
        name: '現金',
        market: '',
        currency: 'TWD',
        quantity: 0,
      };
    case 'LIABILITY':
      return {
        _key: nextKey(),
        assetType: 'LIABILITY',
        symbol: '',
        name: '信貸',
        market: '',
        currency: 'TWD',
        quantity: 0,
      };
    case 'REAL_ESTATE':
      return {
        _key: nextKey(),
        assetType: 'REAL_ESTATE',
        symbol: '',
        name: '不動產',
        market: '',
        currency: 'TWD',
        quantity: 0,
        valuationMethod: '市價',
      };
    case 'FUND':
      return {
        _key: nextKey(),
        assetType: 'FUND',
        symbol: '',
        name: '',
        market: 'FUND',
        currency: 'TWD',
        // Funds are commonly bought with TWD even when the NAV is foreign-priced.
        costCurrency: 'TWD',
        quantity: 0,
        costBasis: null,
      };
  }
};

export const toRow = (p: PositionInput): EditorRow => ({ ...p, _key: nextKey() });

/** A row the user added but never filled in — silently dropped on save. */
export const isRowEmpty = (r: EditorRow): boolean =>
  r.assetType === 'CASH' || r.assetType === 'LIABILITY' || r.assetType === 'REAL_ESTATE'
    ? !r.quantity
    : !r.symbol && !r.quantity;

/** Validation message for a non-empty row, or null when valid. */
export const rowError = (r: EditorRow): string | null => {
  if (r.assetType === 'CASH' || r.assetType === 'LIABILITY' || r.assetType === 'REAL_ESTATE') {
    return r.quantity > 0 ? lengthProblem(r) : '請輸入金額';
  }
  if (!r.symbol) {
    return r.assetType === 'FUND' ? '請選擇基金' : '請選擇股票';
  }
  if (!(r.quantity > 0)) {
    return r.assetType === 'FUND' ? '請輸入單位數' : '請輸入股數';
  }
  return lengthProblem(r);
};
