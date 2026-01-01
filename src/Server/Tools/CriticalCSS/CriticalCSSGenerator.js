import { generate } from 'critical';
import fs from 'fs';
import path from 'path';

import puppeteer from 'puppeteer';

const browser = await puppeteer.launch();
const page = await browser.newPage();

// Login
await page.goto('http://localhost:5146/Account/Login');
await page.type('#username', 'admin');
await page.type('#password', 'UnsafePractice.67');
await page.click('button[type=submit]');
await page.waitForNavigation();

// Extract cookies
const cookies = await page.cookies();
await browser.close();

async function generateCriticalCSSForViews() {
  const viewsDir = '../../Views';
  
  // Helper function to get all .cshtml files recursively
  function getAllCshtmlFiles(dir) {
    let results = [];
    const list = fs.readdirSync(dir);
    
    list.forEach(file => {
      const filePath = path.join(dir, file);
      const stat = fs.statSync(filePath);
      if (stat && stat.isDirectory()) {
        // Recursively get files from subdirectories
        results = results.concat(getAllCshtmlFiles(filePath));
      } else if (file.endsWith('.cshtml') && filePath.search("/_") == -1) {
        results.push(filePath);
      }
    });
    
    return results;
  }
  
  // Helper function to convert file path to URL path
  function filePathToUrlPath(filePath) {
    // Remove 'Views/' prefix
    let relativePath = filePath.replace(/^Views[\/\\]/, '');
    
    // Remove .cshtml extension
    relativePath = relativePath.replace(/\.cshtml$/, '');
    
    // Convert to URL format (replace \ with / and capitalize first letter)
    const urlPath = relativePath
      .split(/[\/\\]/)
      .map((segment, index) => 
        index === 0 ? segment : segment.charAt(0).toUpperCase() + segment.slice(1)
      )
      .join('/');
    
    // Handle the case where we have a single file (like Index.cshtml)
    if (relativePath.includes('/')) {
      // Convert to URL path format: Views/Home/Index.cshtml -> /Home/Index
      return '/' + relativePath.replace(/\\/g, '/').replace(/\.cshtml$/, '');
    } else {
      // For files directly in Views folder (like Views/Index.cshtml)
      return '/' + relativePath.replace(/\.cshtml$/, '');
    }
  }
  
  // Get all .cshtml files
  const cshtmlFiles = getAllCshtmlFiles(viewsDir);
  const criticalCssDir = '.';
//   if (!fs.existsSync(criticalCssDir)) {
//     fs.mkdirSync(criticalCssDir, { recursive: true });
//   }
  
  // Process each file
  for (const file of cshtmlFiles) {
    try {
      const urlPath = filePathToUrlPath(file).replace("../", "").replace("../", "").replace("/Views", "");
      
      // Generate critical CSS
      await generate({
        src: `http://localhost:5146${urlPath}`,
        inline: false,
        width: 1920,
        height: 1080,
        penthouse: {
          customHeaders: {
            cookie: cookies.map(c => `${c.name}=${c.value}`).join('; ')
          },
          forceExclude: ['.btn'], // Otherwise buttons end up colorless and .btn overrides other classes like .btn-warning, etc. - so it has to be force-excluded here and re-added later
          forceInclude: [
            '[data-bs-theme=dark]',
            '.navbar',
            '.col-md-4',
            '.visually-hidden', // visually hidden headings
            '.bi-info-circle-fill', '.text-info', // info icon
            '.container', '.col-md-6', '.row', '.g-4', '.row>*',
            'p', '.fs-3', '.py-4', // title
            '.mb-4',
            '.card', '.card-body', '.p-2', // card
            'h2', '.card-title', '.fs-5', // card - title
            '.d-flex', '.justify-content-between', '.mt-2', // card - content
            '.progress', '.mt-3', // card - progress bar
            '.list-group', '.list-group-flush', '.list-group-item', '.list-group-flush>.list-group-item', '.list-group-flush>.list-group-item:last-child', '.badge', '.bg-warning', '.bg-success', '.h-100', // card - health check list
            '.btn-primary', '.btn-warning', '.btn-danger', '.btn-info', // Searchdomains buttons
            '.col-md-8', '.sidebar',
            '.mb-0', '.mb-2', '.align-items-center',
            'h3', '.col-md-3', '.col-md-2', '.text-nowrap', '.overflow-auto'
          ]
        },
        target: {
          css: path.join(criticalCssDir, "../../CriticalCSS/" + urlPath.replace(/\//g, '.').replace(/^\./, '').replace("...", "") + '.css')
        }
      });
      
      console.log(`Critical CSS generated for: ${urlPath}`);
    } catch (err) {
      console.error(`Error processing ${file}:`, err);
    }
  }
  
  console.log('All critical CSS files generated!');
}

// Run the function
generateCriticalCSSForViews().catch(console.error);