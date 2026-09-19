import { render, screen } from '@testing-library/react';
import { describe, test, expect } from 'vitest';
import Providers from '../src/app/Providers';

describe('Providers', () => {
  test('renders children', () => {
    render(
      <Providers>
        <span>hi</span>
      </Providers>
    );
    expect(screen.getByText('hi')).toBeInTheDocument();
  });
});
