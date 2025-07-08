import { Component, OnInit, OnDestroy, HostListener, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpClientModule } from '@angular/common/http';
import { RouterModule } from '@angular/router';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import * as signalR from '@microsoft/signalr';

// Material imports
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

interface Document {
  id: number;
  fileName: string;
  blobPath: string;
  isSummaryDone: boolean;
  summary?: string;
  uploadedUtc: string;
  uploadedBy: string;
  hasSummary: boolean;
  summaryPreview?: string;
}

interface Notification {
  type: 'success' | 'error' | 'info';
  message: string;
}

interface DocumentUpdate {
  id: number;
  fileName: string;
  summary: string;
  isSummaryDone: boolean;
  updatedUtc: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule, 
    FormsModule, 
    RouterModule, 
    HttpClientModule,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatSlideToggleModule,
    MatSidenavModule,
    MatTooltipModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './app.html',
  styleUrls: ['./app.scss']
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly API_BASE = 'http://localhost:5259/api';
  private autoRefreshInterval: any;
  private hubConnection: signalR.HubConnection | null = null;
  private dateUpdateInterval: any;
  
  // Data
  documents: Document[] = [];
  paginatedDocuments: Document[] = [];
  
  // Upload state
  selectedFile: File | null = null;
  uploading = false;
  loading = false;
  isDragOver = false;
  
  // UI state
  notification: Notification | null = null;
  autoRefresh = true;
  signalRConnected = false;
  deletingDocuments: Set<number> = new Set();
  downloadingDocuments: Set<number> = new Set();
  downloadingSummaries: Set<number> = new Set();
  
  // AI Summary Modal state
  showDocumentModal = false;
  selectedDocumentForView: Document | null = null;
  
  // Document Viewer Modal state - UPDATED to include DOCX
  showViewerModal = false;
  selectedDocumentForViewer: Document | null = null;
  viewerLoading = false;
  documentUrl = '';
  safeDocumentUrl: SafeResourceUrl | null = null;
  documentContent = '';
  viewerType: 'pdf' | 'image' | 'text' | 'docx' | 'unsupported' = 'pdf';
  hasViewerError = false;

  // Helper methods for file handling
  public getFileExtension(fileName: string): string {
    return fileName.split('.').pop() || '';
  }
  
  // Pagination
  currentPage = 1;
  pageSize = 5;
  totalPages = 1;

  constructor(private http: HttpClient, private sanitizer: DomSanitizer) {}

  ngOnInit() {
    this.loadDocuments();
    this.setupSignalR();
    this.startAutoRefresh();
    this.startDateUpdateInterval();
  }

  ngOnDestroy() {
    this.stopAutoRefresh();
    this.disconnectSignalR();
    this.stopDateUpdateInterval();
  }

  // Enhanced keyboard event handler for both modals
  @HostListener('keydown', ['$event'])
  onKeyDown(event: KeyboardEvent) {
    // AI Summary modal shortcuts
    if (this.showDocumentModal && this.selectedDocumentForView) {
      switch(event.key) {
        case 'd':
        case 'D':
          if (event.ctrlKey || event.metaKey) {
            event.preventDefault();
            this.downloadSummaryFromModal();
          }
          break;
        case 'Escape':
          this.closeDocumentModal();
          break;
      }
    }
    
    // Document viewer modal shortcuts
    if (this.showViewerModal && this.selectedDocumentForViewer) {
      switch(event.key) {
        case 'Escape':
          this.closeViewer();
          break;
        case 'd':
        case 'D':
          if (event.ctrlKey || event.metaKey) {
            event.preventDefault();
            this.downloadDocument(this.selectedDocumentForViewer);
          }
          break;
      }
    }
  }

  // SignalR Setup
  private async setupSignalR() {
    try {
      this.hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(`${this.API_BASE.replace('/api', '')}/documentHub`, {
          withCredentials: false
        })
        .withAutomaticReconnect([0, 2000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information)
        .build();

      // Set up event listeners
      this.hubConnection.on('DocumentUploaded', (document: DocumentUpdate) => {
        console.log('📡 DocumentUploaded received:', document);
        this.handleDocumentUploaded(document);
        this.showNotification('info', `New document uploaded: ${document.fileName}`);
      });

      this.hubConnection.on('SummaryReady', (document: DocumentUpdate) => {
        console.log('📡 SummaryReady received:', document);
        this.handleSummaryReady(document);
        this.showNotification('success', `Summary ready for: ${document.fileName}`);
      });

      this.hubConnection.on('DocumentDeleted', (document: DocumentUpdate) => {
        console.log('📡 DocumentDeleted received:', document);
        this.handleDocumentDeleted(document);
        this.showNotification('info', `Document deleted: ${document.fileName}`);
      });

      // Connection state handlers
      this.hubConnection.onreconnecting(() => {
        console.log('SignalR reconnecting...');
        this.signalRConnected = false;
      });

      this.hubConnection.onreconnected(() => {
        console.log('SignalR reconnected');
        this.signalRConnected = true;
      });

      this.hubConnection.onclose(() => {
        console.log('SignalR connection closed');
        this.signalRConnected = false;
      });

      // Start connection
      await this.hubConnection.start();
      console.log('✅ SignalR connected successfully');
      this.signalRConnected = true;
      
    } catch (error) {
      console.error('❌ SignalR connection failed:', error);
      this.signalRConnected = false;
      
      // Retry connection after 5 seconds
      setTimeout(() => this.setupSignalR(), 5000);
    }
  }

  private async disconnectSignalR() {
    if (this.hubConnection) {
      await this.hubConnection.stop();
    }
  }

  // SignalR Event Handlers
  private handleDocumentUploaded(document: DocumentUpdate) {
    // Add new document to the beginning of the list
    const newDoc: Document = {
      id: document.id,
      fileName: document.fileName,
      blobPath: '',
      isSummaryDone: document.isSummaryDone,
      summary: document.summary,
      uploadedUtc: document.updatedUtc,
      uploadedBy: 'User',
      hasSummary: !!document.summary,
      summaryPreview: document.summary
    };

    // Check if document already exists
    const existingIndex = this.documents.findIndex(d => d.id === document.id);
    if (existingIndex === -1) {
      this.documents.unshift(newDoc);
      this.updatePagination();
    }
  }

  private handleSummaryReady(document: DocumentUpdate) {
    // Update existing document with summary
    const index = this.documents.findIndex(d => d.id === document.id);
    if (index !== -1) {
      this.documents[index] = {
        ...this.documents[index],
        isSummaryDone: document.isSummaryDone,
        summary: document.summary,
        hasSummary: true,
        summaryPreview: document.summary.length > 100 
          ? document.summary.substring(0, 100) + '...' 
          : document.summary
      };
      this.updatePagination();
    }
  }

  private handleDocumentDeleted(document: DocumentUpdate) {
    // Remove document from list
    this.documents = this.documents.filter(d => d.id !== document.id);
    this.deletingDocuments.delete(document.id);
    
    // Close modals if the deleted document was being viewed
    if (this.selectedDocumentForView?.id === document.id) {
      this.closeDocumentModal();
    }
    if (this.selectedDocumentForViewer?.id === document.id) {
      this.closeViewer();
    }
    
    // Adjust pagination if needed
    if (this.paginatedDocuments.length === 0 && this.currentPage > 1) {
      this.currentPage--;
    }
    this.updatePagination();
  }

  // Auto-refresh functionality
  startAutoRefresh() {
    if (this.autoRefreshInterval) {
      clearInterval(this.autoRefreshInterval);
    }
    
    if (this.autoRefresh) {
      this.autoRefreshInterval = setInterval(() => {
        if (!this.uploading && this.deletingDocuments.size === 0 && this.downloadingDocuments.size === 0) {
          this.loadDocuments();
        }
      }, 10000);
    }
  }

  stopAutoRefresh() {
    if (this.autoRefreshInterval) {
      clearInterval(this.autoRefreshInterval);
      this.autoRefreshInterval = null;
    }
  }

  toggleAutoRefresh() {
    if (this.autoRefresh) {
      this.startAutoRefresh();
    } else {
      this.stopAutoRefresh();
    }
  }

  // Load documents from API
  async loadDocuments() {
    this.loading = true;
    try {
      const response = await this.http.get<Document[]>(`${this.API_BASE}/documents`).toPromise();
      this.documents = response || [];
      this.updatePagination();
      console.log('Documents loaded:', this.documents.length);
    } catch (error) {
      this.showNotification('error', 'Failed to load documents');
      console.error('Error loading documents:', error);
    } finally {
      this.loading = false;
    }
  }

  // UPDATED: Enhanced getViewerType to support DOCX
  private getViewerType(fileName: string): 'pdf' | 'image' | 'text' | 'docx' | 'unsupported' {
    const ext = this.getFileExtension(fileName).toLowerCase();
    if (ext === 'pdf') return 'pdf';
    if (['jpg', 'jpeg', 'png', 'gif'].includes(ext)) return 'image';
    if (['txt', 'csv', 'json', 'md'].includes(ext)) return 'text';
    if (['docx', 'doc'].includes(ext)) return 'docx';  // NEW: DOCX support
    return 'unsupported';
  }

  // UPDATED: Enhanced validateDocumentUrl to handle DOCX
  private async validateDocumentUrl(url: string, type: 'pdf' | 'image' | 'docx'): Promise<boolean> {
    if (!url) return false;
    
    if (type === 'pdf') {
      return url.toLowerCase().includes('view-proxy') || url.toLowerCase().includes('.pdf');
    } else if (type === 'docx') {
      return url.toLowerCase().includes('docx-html');
    } else if (type === 'image') {
      try {
        await new Promise((resolve, reject) => {
          const img = new Image();
          img.onload = resolve;
          img.onerror = reject;
          img.src = url;
        });
        return true;
      } catch {
        return false;
      }
    }
    return false;
  }

  // UPDATED: Enhanced openDocumentViewer to handle DOCX files
  async openDocumentViewer(doc: Document): Promise<void> {
    try {
      console.log('📖 Opening document viewer for:', doc.fileName);
      
      this.selectedDocumentForViewer = doc;
      this.showViewerModal = true;
      this.viewerLoading = true;
      this.viewerType = this.getViewerType(doc.fileName);
      this.documentUrl = '';
      this.safeDocumentUrl = null;
      this.documentContent = '';
      this.hasViewerError = false;

      if (this.viewerType === 'text') {
        console.log('📃 Loading text content...');
        // For text files, fetch the content directly
        const response = await this.http.get(`${this.API_BASE}/documents/${doc.id}/content`, { responseType: 'text' }).toPromise();
        if (!response) {
          throw new Error('Empty document content');
        }
        this.documentContent = response;
        console.log('✅ Text content loaded successfully');
        
      } else if (this.viewerType === 'pdf') {
        console.log('📄 Loading PDF via proxy...');
        // Use proxy URL directly for PDFs
        this.documentUrl = `${this.API_BASE}/documents/${doc.id}/view-proxy`;
        this.safeDocumentUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.documentUrl);
        console.log('✅ PDF proxy URL generated:', this.documentUrl);
        
      } else if (this.viewerType === 'docx') {
        console.log('📝 Loading DOCX as HTML...');
        // NEW: Handle DOCX files by converting to HTML
        this.documentUrl = `${this.API_BASE}/documents/${doc.id}/docx-html`;
        this.safeDocumentUrl = this.sanitizer.bypassSecurityTrustResourceUrl(this.documentUrl);
        console.log('✅ DOCX HTML URL generated:', this.documentUrl);
        
      } else if (this.viewerType === 'image') {
        console.log('🖼️ Loading image...');
        // For images, get the view URL
        const response = await this.http.get(`${this.API_BASE}/documents/${doc.id}/view-url`).toPromise() as { url: string };
        if (!response?.url) {
          throw new Error('Invalid document URL');
        }
        this.documentUrl = response.url;
        
        // Validate the image URL
        const isValid = await this.validateDocumentUrl(this.documentUrl, this.viewerType);
        if (!isValid) {
          throw new Error('Invalid image URL');
        }
        console.log('✅ Image URL validated:', this.documentUrl);
      }
      
    } catch (error) {
      console.error('❌ Error loading document:', error);
      this.hasViewerError = true;
      this.showNotification('error', 'Failed to load document preview. Please try downloading the file instead.');
      this.viewerType = 'unsupported';
    } finally {
      this.viewerLoading = false;
    }
  }

  closeViewer(): void {
    console.log('📖 Closing document viewer');
    this.showViewerModal = false;
    this.selectedDocumentForViewer = null;
    this.documentUrl = '';
    this.safeDocumentUrl = null;
    this.documentContent = '';
    this.hasViewerError = false;
  }

  // UPDATED: Enhanced canPreviewDocument to include DOCX
  canPreviewDocument(doc: Document): boolean {
    const supportedTypes = ['pdf', 'image', 'text', 'docx'];
    return supportedTypes.includes(this.getViewerType(doc.fileName));
  }

  // Image load handlers
  onImageLoad() {
    console.log('✅ Image loaded successfully');
  }

  onImageError() {
    console.log('❌ Image failed to load');
    this.hasViewerError = true;
    this.showNotification('error', 'Failed to load image preview');
  }

  // Download original document functionality
  async downloadDocument(doc: Document) {
    this.downloadingDocuments.add(doc.id);
    
    try {
      console.log('📥 Starting download for:', doc.fileName);
      
      this.showNotification('info', `Preparing download for ${doc.fileName}...`);
      
      const downloadUrl = `${this.API_BASE}/documents/${doc.id}/download`;
      
      const response = await fetch(downloadUrl);
      
      if (!response.ok) {
        throw new Error(`Download failed: ${response.status} ${response.statusText}`);
      }
      
      const blob = await response.blob();
      
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = doc.fileName;
      link.style.display = 'none';
      
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      
      console.log('✅ Download completed for:', doc.fileName);
      this.showNotification('success', `Download completed: ${doc.fileName}`);
      
    } catch (error: any) {
      console.error('❌ Download failed:', error);
      this.showNotification('error', `Download failed: ${error.message || 'Unknown error'}`);
    } finally {
      this.downloadingDocuments.delete(doc.id);
    }
  }

  // Download AI Summary as text file
  downloadSummaryFromModal() {
    if (!this.selectedDocumentForView) return;
    
    const doc = this.selectedDocumentForView;
    this.downloadingSummaries.add(doc.id);
    
    try {
      console.log('📝 Starting AI summary download for:', doc.fileName);
      
      this.showNotification('info', `Preparing AI summary download for ${doc.fileName}...`);
      
      const summaryText = doc.summary || 'No summary available';
      const summaryContent = this.formatSummaryForDownload(doc, summaryText);
      const blob = new Blob([summaryContent], { type: 'text/plain;charset=utf-8' });
      
      const originalName = doc.fileName.split('.')[0];
      const summaryFileName = `${originalName}_AI_Summary.txt`;
      
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = summaryFileName;
      link.style.display = 'none';
      
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      
      console.log('✅ AI summary download completed for:', doc.fileName);
      this.showNotification('success', `AI summary downloaded: ${summaryFileName}`);
      
    } catch (error: any) {
      console.error('❌ AI summary download failed:', error);
      this.showNotification('error', `AI summary download failed: ${error.message || 'Unknown error'}`);
    } finally {
      this.downloadingSummaries.delete(doc.id);
    }
  }

  // Format summary content for download
  private formatSummaryForDownload(doc: Document, summaryText: string): string {
    const currentDate = new Date().toLocaleString();
    
    return `AI SUMMARY REPORT
=====================================================

Document Name: ${doc.fileName}
Generated Date: ${currentDate}
Uploaded Date: ${this.formatDate(doc.uploadedUtc)}
Uploaded By: ${doc.uploadedBy}
Document ID: ${doc.id}

=====================================================
AI SUMMARY:
=====================================================

${summaryText}

=====================================================
END OF SUMMARY
=====================================================

Generated by Document Hub AI System
Export Date: ${currentDate}
`;
  }

  // Delete document
  async deleteDocument(doc: Document) {
    const confirmDelete = confirm(`Are you sure you want to delete "${doc.fileName}"? This action cannot be undone.`);
    if (!confirmDelete) {
      return;
    }

    this.deletingDocuments.add(doc.id);

    try {
      console.log('🗑️ Deleting document:', doc.fileName);
      
      const response = await this.http.delete(`${this.API_BASE}/documents/${doc.id}`).toPromise();
      console.log('✅ Delete successful:', response);
      
    } catch (error: any) {
      console.error('❌ Delete failed:', error);
      this.deletingDocuments.delete(doc.id);
      
      let errorMessage = 'Failed to delete document';
      if (error.error?.error) {
        errorMessage = error.error.error;
      } else if (error.message) {
        errorMessage = error.message;
      }
      
      this.showNotification('error', errorMessage);
    }
  }

  // Check if document is being deleted
  isDeleting(docId: number): boolean {
    return this.deletingDocuments.has(docId);
  }

  // Check if document is being downloaded
  isDownloading(docId: number): boolean {
    return this.downloadingDocuments.has(docId);
  }

  // Check if summary is being downloaded
  isDownloadingSummary(docId: number): boolean {
    return this.downloadingSummaries.has(docId);
  }

  // AI Summary Modal functionality
  viewDocument(doc: Document) {
    if (!doc.isSummaryDone) {
      this.showNotification('error', 'Summary not ready yet. Please wait for AI processing to complete.');
      return;
    }

    this.selectedDocumentForView = doc;
    this.showDocumentModal = true;
  }

  closeDocumentModal() {
    this.showDocumentModal = false;
    this.selectedDocumentForView = null;
  }

  // Pagination
  updatePagination() {
    this.totalPages = Math.ceil(this.documents.length / this.pageSize);
    const startIndex = (this.currentPage - 1) * this.pageSize;
    const endIndex = startIndex + this.pageSize;
    this.paginatedDocuments = this.documents.slice(startIndex, endIndex);
  }

  nextPage() {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.updatePagination();
    }
  }

  previousPage() {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.updatePagination();
    }
  }

  // Drag and drop handlers
  onDragOver(event: DragEvent) {
    event.preventDefault();
    this.isDragOver = true;
  }

  onDragLeave(event: DragEvent) {
    event.preventDefault();
    this.isDragOver = false;
  }

  onDrop(event: DragEvent) {
    event.preventDefault();
    this.isDragOver = false;
    
    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.validateAndSetFile(files[0]);
    }
  }

  // Handle file selection
  onFileSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      this.validateAndSetFile(file);
    }
  }

  // Validate and set selected file
  validateAndSetFile(file: File) {
    // Check file size (10MB limit)
    if (file.size > 10 * 1024 * 1024) {
      this.showNotification('error', 'File size must be less than 10MB');
      return;
    }

    // Check file type
    const allowedTypes = ['.pdf', '.docx', '.txt', '.jpg', '.png', '.jpeg'];
    const fileExtension = '.' + file.name.split('.').pop()?.toLowerCase();
    
    if (!allowedTypes.includes(fileExtension)) {
      this.showNotification('error', `File type ${fileExtension} is not supported`);
      return;
    }

    this.selectedFile = file;
    this.showNotification('success', `File selected: ${file.name}`);
  }

  // Remove selected file
  removeFile() {
    this.selectedFile = null;
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    if (fileInput) fileInput.value = '';
  }

  // Upload file
  async uploadFile() {
    if (!this.selectedFile) {
      this.showNotification('error', 'Please select a file first');
      return;
    }

    this.uploading = true;
    const formData = new FormData();
    formData.append('file', this.selectedFile);

    try {
      console.log('Uploading file:', this.selectedFile.name);

      const response = await this.http.post(`${this.API_BASE}/documents/upload`, formData).toPromise();
      
      console.log('Upload successful:', response);
      
      this.showNotification('success', `File "${this.selectedFile.name}" uploaded successfully! AI summary generation started.`);
      this.selectedFile = null;
      
      const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
      if (fileInput) fileInput.value = '';
      
    } catch (error: any) {
      console.error('Upload failed:', error);
      
      let errorMessage = 'Upload failed';
      if (error.error?.error) {
        errorMessage = error.error.error;
      } else if (error.message) {
        errorMessage = error.message;
      }
      
      this.showNotification('error', errorMessage);
    } finally {
      this.uploading = false;
    }
  }

  // UPDATED: Enhanced getFileIcon to better handle DOCX
  getFileIcon(fileName: string): string {
    const extension = fileName.split('.').pop()?.toLowerCase();
    switch (extension) {
      case 'pdf': return '📄';
      case 'docx': return '📝';
      case 'doc': return '📝';
      case 'txt': return '📃';
      case 'csv': return '📊';
      case 'json': return '🔧';
      case 'md': return '📝';
      case 'jpg': case 'jpeg': case 'png': case 'gif': return '🖼️';
      default: return '📁';
    }
  }

  getFileTypeIcon(fileName: string): string {
    const ext = fileName.toLowerCase().split('.').pop() || '';
    switch (ext) {
      case 'pdf':
        return 'picture_as_pdf';
      case 'doc':
      case 'docx':
        return 'article';
      case 'txt':
        return 'text_snippet';
      case 'jpg':
      case 'jpeg':
      case 'png':
      case 'gif':
        return 'image';
      default:
        return 'description';
    }
  }

  // Format file size
  formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  // Format date
  formatDate(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diff = Math.floor((now.getTime() - date.getTime()) / 1000); // difference in seconds

    if (diff < 60) {
      return 'just now';
    } else if (diff < 3600) {
      const minutes = Math.floor(diff / 60);
      return `${minutes} ${minutes === 1 ? 'minute' : 'minutes'} ago`;
    } else if (diff < 86400) {
      const hours = Math.floor(diff / 3600);
      return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
    } else if (diff < 604800) { // 7 days
      const days = Math.floor(diff / 86400);
      return `${days} ${days === 1 ? 'day' : 'days'} ago`;
    } else {
      return date.toLocaleDateString('en-US', { 
        year: 'numeric', 
        month: 'short', 
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    }
  }

  private startDateUpdateInterval() {
    // Update the relative times every minute
    this.dateUpdateInterval = setInterval(() => {
      // Force view update by creating a new reference to the documents array
      this.documents = [...this.documents];
    }, 60000); // 60 seconds
  }

  private stopDateUpdateInterval() {
    if (this.dateUpdateInterval) {
      clearInterval(this.dateUpdateInterval);
      this.dateUpdateInterval = null;
    }
  }

  // Show notification
  showNotification(type: 'success' | 'error' | 'info', message: string) {
    this.notification = { type, message };
    // Auto-hide notification after 5 seconds
    setTimeout(() => {
      this.notification = null;
    }, 5000);
  }

  // Close notification
  closeNotification() {
    this.notification = null;
  }

  // Open PDF in new tab using proxy URL
  public openPdfInNewTab(): void {
    if (this.documentUrl) {
      window.open(this.documentUrl, '_blank');
    } else if (this.selectedDocumentForViewer) {
      // Fallback: use proxy URL directly
      const proxyUrl = `${this.API_BASE}/documents/${this.selectedDocumentForViewer.id}/view-proxy`;
      window.open(proxyUrl, '_blank');
    }
  }

  // Open DOCX in new tab
  public openDocxInNewTab(): void {
    if (this.documentUrl) {
      window.open(this.documentUrl, '_blank');
    } else if (this.selectedDocumentForViewer) {
      // Fallback: use DOCX HTML URL directly
      const docxUrl = `${this.API_BASE}/documents/${this.selectedDocumentForViewer.id}/docx-html`;
      window.open(docxUrl, '_blank');
    }
  }

  // NEW: Open Image in new tab
  public openImageInNewTab(): void {
    if (this.documentUrl) {
      window.open(this.documentUrl, '_blank');
    } else if (this.selectedDocumentForViewer) {
      // Fallback: use image URL directly
      const imageUrl = `${this.API_BASE}/documents/${this.selectedDocumentForViewer.id}/view-proxy`;
      window.open(imageUrl, '_blank');
    }
  }
}