# Letter Translation — TODO

## Deployment
- [ ] Verify nginx `resolver 127.0.0.11` works in Azure Container Apps (may need adjustment)
- [ ] Consider setting backend `--min-replicas 1` to avoid cold-start 502s from frontend proxy
- [ ] Confirm Google OAuth credentials have the deployed frontend URL in authorized origins

## Image Carousel (Job Detail Page)
- [ ] **Backend: Thumbnail endpoint** — Add an API endpoint (e.g. `GET /api/jobs/{jobId}/files/{fileName}/thumbnail`) that returns a resized image (e.g. 200px wide) for use in the carousel. Consider caching generated thumbnails in blob storage.
- [ ] **Frontend: Carousel component** — Build a horizontal carousel/filmstrip of image thumbnails on the job detail page. Should support scrolling/navigation through multiple images.
- [ ] **Frontend: Full-size image preview** — On hover (or click) of a carousel thumbnail, display the full-resolution image in a large overlay/modal that fills most of the viewport height.
