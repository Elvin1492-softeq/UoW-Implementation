using Core.Models.DocumentsModel;
using Core.Models.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Repository.Infrastructure;
using Repository.Repositories.Documents;
using Service.Base;
using Service.Services.Documents.Dtos;
using Service.Utils;
using SpecImplementation.Standart;
using Spire.Doc.Documents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GemBox.Document;

namespace Service.Services.Documents
{
    public interface IDocumentsService
    {
        Task<List<GetAllDocumentsResponse>> GetAllDocuments();
        Task<ListResult<GetAllDocumentsResponse>> GetAllPagingDocuments(int offset, int limit, Guid caseId);
        Task<GetDocumentByIdResponse> GetDocumentById(Guid id);
        Task<Guid> InsertDocumentAsync(InsertorUpdateDocumentInput input);
        Task<Guid> UpdateDocumentAsync(InsertorUpdateDocumentInput input);
        Task<bool> DeleteDocumentAsync(Guid id);
        Task<Guid> InsertDocumentPrimaryAsync(InsertPrimaryDocumentInput input);
        Task<GetDynamicDocumentResponse> GetDynamicDocumentGenerator(Guid templateId);
        Task<Guid> SetDynamicDocument(SetDynamicDocumentInput input);
        Task<bool> ChangeToPdf(Guid id);
    }
    public class DocumentsService : BaseAppService, IDocumentsService
    {
        private readonly IDocumentsRepository _documentsRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDocumentTemplatesRepository documentTemplatesRepository;
        private readonly IConfiguration configuration;

        public DocumentsService(IDocumentsRepository documentsRepository, IUnitOfWork unitOfWork, IDocumentTemplatesRepository documentTemplatesRepository, IConfiguration configuration, IHttpContextAccessor httpContext) : base(httpContext)
        {
            _documentsRepository = documentsRepository;
            _unitOfWork = unitOfWork;
            documentTemplatesRepository = documentTemplatesRepository;
            configuration = configuration;
        }

        public async Task<bool> DeleteDocumentAsync(Guid id)
        {

            using (var trx = _unitOfWork.BeginTransaction())
            {
                try
                {
                    await _documentsRepository.DeleteAsync(id);
                    trx.Commit();
                    return true;
                }
                catch (Exception)
                {
                    trx.Rollback();
                    return false;
                }
            }

        }

        public async Task<bool> ChangeToPdf(Guid id)
        {
            try
            {
                var doc= await GetDocumentById(id);
                ComponentInfo.SetLicense("FREE-LIMITED-KEY");
                DocumentModel document = DocumentModel.Load("wwwroot/" + doc.FileName);
                document.Save(Path.ChangeExtension("wwwroot/" + doc.FileName, ".pdf"));
                return true;
            }
            catch (Exception ex)
            {

                return false;
            }


        }


        public async Task<List<GetAllDocumentsResponse>> GetAllDocuments()
        {
            var response = (await _documentsRepository.GetAll()).Select(i => new GetAllDocumentsResponse
            {
                Id = i.Id,
                CaseName = i.Case.Name,
                DocumentTemplateName = i.DocumentTemplate.Name,
                DocumentTypeName = i.DocumentType.Name,
                FileUrl = i.FileUrl
            }).ToList();

            return response;
        }

        public async Task<ListResult<GetAllDocumentsResponse>> GetAllPagingDocuments(int offset, int limit, Guid caseId)
        {
            var data = (await _documentsRepository.GetAllPaging(offset, limit, caseId));
            var response = data.List.Select(i => new GetAllDocumentsResponse
            {
                Id = i.Id,
                CaseName = i.Case.Name,
                DocumentTemplateName = i.DocumentTemplate.Name,
                DocumentTypeName = i.DocumentType.Name,
                FileUrl = i.FileUrl,
                FileName = i.DocumentTemplate.FileName
            }
             );
            return new ListResult<GetAllDocumentsResponse>
            {
                List = response,
                TotalCount = data.TotalCount
            };
        }

        public async Task<GetDocumentByIdResponse> GetDocumentById(Guid id)
        {
            var result = await _documentsRepository.GetById(id);
            var response = new GetDocumentByIdResponse
            {
                Id = result.Id,
                CaseName = result.Case.Name,
                DocumentTemplateName = result.DocumentTemplate.Name,
                DocumentTypeName = result.DocumentType.Name,
                //FileUrl = result.FileUrl
                FileUrl = $"http://localhost:5001/img/testpdf.pdf",
                DocumetTemplateId = result.DocumentTemplate.Id,
                CaseId = result.Case.Id,
                DocumentTypeId = result.DocumentType.Id,
                FileName=result.DocumentTemplate.FileName
            };
            return response;
        }

        public async Task<GetDynamicDocumentResponse> GetDynamicDocumentGenerator(Guid templateId)
        {
            try
            {
                var (bookmarks, bookmarkNavigator, document) = await GetBookmarks(templateId.ToString());
                var templateFileUrl = (await documentTemplatesRepository.GetByIdAsync(templateId.ToString())).FileName;
                var fileName = Guid.NewGuid().ToString() + ".pdf";
                var saveFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName);

                document.SaveToFile(saveFilePath, Spire.Doc.FileFormat.PDF);
                var response = new GetDynamicDocumentResponse
                {
                    Bookmarks = bookmarks,
                    TemplateFileUrl = $"http://localhost:5001/documents/{fileName}"
                };

                return response;
            }
            catch (Exception ex)
            {

                throw ex;
            }
        }

        public async Task<Guid> SetDynamicDocument(SetDynamicDocumentInput input)
        {
            using (var trx = _unitOfWork.BeginTransaction())
            {
                Guid response = Guid.Empty;
                try
                {
                    var jsonObject = JObject.Parse(input.FormValueAsJson);

                    var (bookmarks, bookmarkNavigator, document) = await GetBookmarks(input.TemplateId);

                    foreach (var bookmark in bookmarks)
                    {
                        bookmarkNavigator.MoveToBookmark(bookmark);
                        bookmarkNavigator.ReplaceBookmarkContent(jsonObject[bookmark].ToString(), true);
                    }

                    var fileName = $"{Guid.NewGuid()}.pdf";
                    var saveFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName);

                    document.SaveToFile(saveFilePath, Spire.Doc.FileFormat.PDF);

                    var databaseFileUrl = $"{configuration["CurrentHostPath"]}documents/{fileName}";

                    Document model = new Document
                    {
                        FileUrl = databaseFileUrl,
                        CaseId = Guid.Parse(input.CaseId),
                        Id = Guid.Parse(input.DocumentId),
                        DocumentTemplateId = Guid.Parse(input.TemplateId),
                        DocumentTypeId = Guid.Parse(input.DocumentTypeId)
                    };
                    response = (await _documentsRepository.UpdateAsync(model)).Id;

                    trx.Commit();
                }
                catch (Exception ex)
                {
                    trx.Rollback();
                }
                return response;
            }

        }

        public async Task<Guid> InsertDocumentAsync(InsertorUpdateDocumentInput input)
        {
            var fullPath = PrepareFileForUpload(input.File);

            Document model = new Document
            {
                CaseId = input.CaseId,
                DocumentTemplateId = input.TemplateId,
                DocumentTypeId = input.TypeId,
                FileUrl = fullPath
            };

            using (var trx = _unitOfWork.BeginTransaction())
            {
                Guid response = Guid.Empty;
                try
                {
                    response = await _documentsRepository.InsertAsync(model);
                    trx.Commit();
                }
                catch (Exception ex)
                {
                    trx.Rollback();
                }
                return response;
            }

        }

        public async Task<Guid> InsertDocumentPrimaryAsync(InsertPrimaryDocumentInput input)
        {
            using (var trx = _unitOfWork.BeginTransaction())
            {
                try
                {
                    if (input.DocumentTypeId == Guid.Empty)
                    {
                        throw new Exception("Document TypeId Can not be null");
                    }

                    var documentTemplateId = await documentTemplatesRepository
                                                        .GetByTypeIdIsActive(input.DocumentTypeId.ToString());

                    if (documentTemplateId == Guid.Empty)
                    {
                        throw new Exception("Document Template not founded");
                    }

                    var model = new Document
                    {
                        CaseId = input.CaseId,
                        DocumentTypeId = input.DocumentTypeId,
                        DocumentTemplateId = documentTemplateId
                    };

                    var insertedResult = await _documentsRepository.InsertPrimaryAsync(model);

                    if (insertedResult == Guid.Empty)
                    {
                        throw new Exception("Not succedded");
                    }

                    trx.Commit();

                    return insertedResult;
                }
                catch (Exception ex)
                {
                    trx.Rollback();
                    throw ex;
                }
            }
        }

        public async Task<Guid> UpdateDocumentAsync(InsertorUpdateDocumentInput input)
        {
            Guid response = Guid.Empty;

            string fullPath = string.Empty;

            var document = await GetDocumentById(input.Id.Value);

            if (!input.Id.HasValue)
            {
                throw new Exception("Id can not be null");
            }

            if (input.File != null)
            {

                if (File.Exists(document.FileUrl))
                {
                    File.Delete(document.FileUrl);
                }
                fullPath = PrepareFileForUpload(input.File);
            }
            Document model = new Document
            {
                Id = input.Id.Value,
                CaseId = input.CaseId,
                DocumentTemplateId = input.TemplateId,
                DocumentTypeId = input.TypeId,
                FileUrl = string.IsNullOrEmpty(fullPath) == false ? fullPath : document.FileUrl
            };

            using (var trx = _unitOfWork.BeginTransaction())
            {
                try
                {
                    response = (await _documentsRepository.UpdateAsync(model)).Id;
                    trx.Commit();
                }
                catch (Exception)
                {
                    trx.Rollback();
                }
                return response;
            }
        }


        #region Privates
        private async Task<(List<string> bookmarks, Spire.Doc.Documents.BookmarksNavigator bookmarkNavigator, Spire.Doc.Document document)> GetBookmarks(string templateId)
        {
            var documentTemplateFileUrl = (await documentTemplatesRepository.GetByIdAsync(templateId.ToString())).FileName;

            var host = configuration["CurrentHostPath"];

            var fileName = documentTemplateFileUrl.Split(host)[1].Split("img/")[1];

            var pureFileUrl = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\img", fileName);

            var document = new Spire.Doc.Document(pureFileUrl, Spire.Doc.FileFormat.Docx);

            var bookmarkNavigator = new Spire.Doc.Documents.BookmarksNavigator(document);

            var bookmarks = bookmarkNavigator.Document.Bookmarks.ToListBookmarks();

            bookmarks.RemoveAt(bookmarks.Count - 1);

            return (bookmarks, bookmarkNavigator, document);
        }

        private string GetFileSaveUrlForDatabase(Spire.Doc.Document document)
        {
            var fileName = $"{Guid.NewGuid()}.pdf";
            var saveFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot\\documents", fileName);

            document.SaveToFile(saveFilePath, Spire.Doc.FileFormat.PDF);

            var databaseFileUrl = $"{configuration["CurrentHostPath"]}documents/{fileName}";

            return databaseFileUrl;
        }
        #endregion
    }
}
