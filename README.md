# Document Hub

A modern document management system with AI-powered summarization and real-time updates.

## Features

- 📄 **Document Upload & Management**
  - Support for multiple file formats (PDF, DOCX, TXT, JPG, PNG)
  - File size limit: 10MB
  - Drag & drop interface
  - Progress tracking
  - File type validation

- 🤖 **AI-Powered Summarization**
  - Automatic document content extraction
  - AI-generated summaries using OpenAI
  - Real-time summary status updates
  - Downloadable summary reports

- 👀 **Document Preview**
  - Built-in PDF viewer
  - DOCX preview support
  - Image viewer
  - Text file preview
  - Fallback download option

- ⚡ **Real-Time Updates**
  - SignalR-powered live updates
  - Document upload notifications
  - Summary completion notifications
  - Document deletion notifications
  - Relative time display

- 📱 **Responsive Design**
  - Modern Material Design
  - Mobile-friendly interface
  - Accessible UI components
  - Keyboard shortcuts support

## Tech Stack

### Frontend
- Angular (Latest version)
- TypeScript
- Angular Material UI
- SignalR Client
- SCSS for styling

### Backend
- ASP.NET Core
- Entity Framework Core
- Azure Blob Storage
- Azure OpenAI Service
- SignalR for real-time communication

## Getting Started

### Prerequisites
- Node.js (v18 or later)
- .NET SDK (v9.0 or later)
- Azure account (for Blob Storage and OpenAI)
- Visual Studio Code or Visual Studio 2022

### Installation

1. **Clone the repository**
   ```powershell
   git clone [repository-url]
   cd doc-hub
   ```

2. **Frontend Setup**
   ```powershell
   # Install dependencies
   npm install

   # Start development server
   npm start
   ```
   The frontend will be available at `http://localhost:4200`

3. **Backend Setup**
   ```powershell
   # Navigate to API project
   cd DocHub.Api

   # Restore packages
   dotnet restore

   # Run the API
   dotnet run
   ```
   The API will be available at `http://localhost:5259`

### Configuration

1. **Azure Configuration**
   Create `appsettings.Development.json` in the `DocHub.Api` folder:
   ```json
   {
     "Azure": {
       "BlobStorage": {
         "ConnectionString": "your-connection-string",
         "ContainerName": "documents"
       },
       "OpenAI": {
         "Endpoint": "your-openai-endpoint",
         "ApiKey": "your-api-key",
         "ModelName": "your-model-name"
       }
     }
   }
   ```

2. **Application Settings**
   The frontend API base URL can be configured in `src/app/app.ts`:
   ```typescript
   private readonly API_BASE = 'http://localhost:5259/api';
   ```

## Project Structure

### Frontend Structure
```
src/
├── app/
│   ├── app.config.ts    # App configuration
│   ├── app.html         # Main template
│   ├── app.routes.ts    # Routing configuration
│   ├── app.scss         # Styles
│   └── app.ts           # Main component logic
└── styles.scss          # Global styles
```

### Backend Structure
```
DocHub.Api/
├── Controllers/         # API endpoints
├── Data/               # Database context
├── Hubs/               # SignalR hubs
├── Models/             # Data models
└── Services/           # Business logic
```

## Features in Detail

### Document Upload
- Drag & drop interface
- File type validation
- Size limit enforcement
- Upload progress tracking
- Real-time status updates

### Document Viewer
- PDF preview using browser's built-in viewer
- DOCX preview using HTML conversion
- Image preview with zoom support
- Text file preview with syntax highlighting
- Fallback download option for unsupported formats

### AI Summary Generation
- Automatic content extraction from documents
- AI-powered summary generation
- Progress tracking
- Real-time completion notifications
- Downloadable summary reports

### Real-Time Updates
- SignalR WebSocket connection
- Automatic reconnection handling
- Live document list updates
- Real-time status notifications
- Connection status indicator

## Keyboard Shortcuts

- `Esc` - Close modals
- `Ctrl/Cmd + D` - Download document/summary in modal view

## Browser Support

- Chrome (latest)
- Firefox (latest)
- Edge (latest)
- Safari (latest)

## Development

### Building for Production
```powershell
# Frontend
npm run build

# Backend
dotnet publish -c Release
```

### Running Tests
```powershell
# Frontend tests
npm test

# Backend tests
dotnet test
```

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Angular team for the amazing framework
- Microsoft for .NET Core
- Azure team for cloud services
- OpenAI for AI capabilities
