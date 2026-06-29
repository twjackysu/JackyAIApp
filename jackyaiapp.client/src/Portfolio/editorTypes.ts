import { AssetType, PositionInput } from '@/apis/portfolioApis/types';

export interface EditorRow extends PositionInput {
  _key: string;
}

let counter = 0;
const nextKey = () => `row_${Date.now()}_${counter++}`;

export const makeRow = (assetType: AssetType): EditorRow => {
  switch (assetType) {
    case 'STOCK':
      return {
        _key: nextKey(),
        assetType: 'STOCK',
        symbol: '',
        name: '',
        market: 'TW',
        currency: 'TWD',
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
    case 'FUND':
      return {
        _key: nextKey(),
        assetType: 'FUND',
        symbol: '',
        name: '',
        market: 'FUND',
        currency: 'TWD',
        quantity: 0,
        unitPrice: null,
      };
  }
};

export const toRow = (p: PositionInput): EditorRow => ({ ...p, _key: nextKey() });
