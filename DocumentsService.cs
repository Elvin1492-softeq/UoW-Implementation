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
        Task<Guid> InsertDocumentAsync(InsertorUpdateDocumentInput input);
        Task<bool> DeleteDocumentAsync(Guid id);
    }
    public class DocumentsService : BaseAppService, IDocumentsService
    {
        private readonly IDocumentsRepository _documentsRepository;
        private readonly IUnitOfWork _unitOfWork;

        public DocumentsService(IDocumentsRepository documentsRepository, IUnitOfWork unitOfWork) : base(httpContext)
        {
            _documentsRepository = documentsRepository;
            _unitOfWork = unitOfWork;
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
    }
}
