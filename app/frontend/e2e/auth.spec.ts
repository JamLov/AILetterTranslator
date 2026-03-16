import { test, expect } from '@playwright/test';

test.describe('Authentication and Dashboard', () => {
  
  test('unauthenticated user is redirected to login', async ({ page }) => {
    // Attempt to visit dashboard without logging in
    await page.goto('/dashboard');
    
    // Should be redirected to login
    await expect(page).toHaveURL(/.*\/login/);
    await expect(page.locator('h1:has-text("Welcome to Letter Translator")')).toBeVisible();
  });

  test('authenticated user can view dashboard', async ({ page }) => {
    // 1. Intercept the call to our jobs backend API and mock the response
    await page.route('**/api/jobs', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]) // Return an empty array for the initial dashboard view
      });
    });

    // 2. Set the auth token in localStorage before navigating
    await page.evaluate(() => {
      localStorage.setItem('userToken', 'fake-e2e-token');
      localStorage.setItem('userId', 'fake-e2e-user-id');
    });

    // 3. Now navigate to the dashboard
    await page.goto('/dashboard');

    // 4. Assert the dashboard loaded successfully
    await expect(page.locator('h2')).toHaveText('My Jobs');
    await expect(page.locator('h3:has-text("Welcome!")')).toBeVisible();
    await expect(page.locator('p:has-text("You haven\'t translated any letters yet.")')).toBeVisible();
  });
});
