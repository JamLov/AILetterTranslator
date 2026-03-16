import { test, expect } from '@playwright/test';

const MOCK_JOB_ID = 'e2e-8a3d-4c3c-9b9c-3e6a7a0a6b4c';
const MOCK_JOB_NAME = 'My E2E Test Job';

test.describe('Job Creation and Viewing Flow', () => {

  test.beforeEach(async ({ page }) => {
    // Set the auth token in localStorage before each test
    await page.goto('/'); // Go to base URL to set localStorage for the correct origin
    await page.evaluate(() => {
      localStorage.setItem('userToken', 'fake-e2e-token');
      localStorage.setItem('userId', 'fake-e2e-user-id');
    });
  });

  test('user can create a new job and see it on the dashboard', async ({ page }) => {
    // Mock the POST request for creating a job
    await page.route('**/api/jobs', async (route, request) => {
      if (request.method() === 'POST') {
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            jobId: MOCK_JOB_ID,
            jobName: MOCK_JOB_NAME,
            createdAt: new Date().toISOString(),
            status: 'Not Started',
            originalFileCount: 1
          })
        });
      } else {
        // For the GET request to list jobs
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([{
            jobId: MOCK_JOB_ID,
            jobName: MOCK_JOB_NAME,
            createdAt: new Date().toISOString(),
            status: 'Not Started',
            originalFileCount: 1
          }])
        });
      }
    });

    // 1. Start on the dashboard
    await page.goto('/dashboard');
    
    // 2. Click "New Job"
    await page.locator('button:has-text("New Job")').click();
    await expect(page).toHaveURL(/.*\/new-job/);

    // 3. Fill out the form
    await page.locator('input[name="jobName"]').fill(MOCK_JOB_NAME);
    await page.locator('textarea[name="notes"]').fill('These are notes from the E2E test.');
    
    // 4. Upload a file
    const fileChooserPromise = page.waitForEvent('filechooser');
    await page.locator('button:has-text("Select Files")').click();
    const fileChooser = await fileChooserPromise;
    await fileChooser.setFiles({
        name: 'test.txt',
        mimeType: 'text/plain',
        buffer: Buffer.from('this is a test file')
    });
    
    // 5. Submit the form
    await page.locator('button[type="submit"]').click();

    // 6. Assert redirection to the dashboard and that the new job is visible
    await expect(page).toHaveURL(/.*\/dashboard/);
    await expect(page.locator(`h3:has-text("${MOCK_JOB_NAME}")`)).toBeVisible();
    await expect(page.locator('span:has-text("Not Started")')).toBeVisible();
  });
});
