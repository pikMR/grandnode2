﻿using Grand.Business.Catalog.Services.ExportImport.Dto;
using Grand.Business.Core.Extensions;
using Grand.Business.Core.Interfaces.Catalog.Collections;
using Grand.Business.Core.Interfaces.Common.Localization;
using Grand.Business.Core.Interfaces.Common.Seo;
using Grand.Business.Core.Interfaces.ExportImport;
using Grand.Business.Core.Interfaces.Storage;
using Grand.Domain.Catalog;
using Grand.Domain.Media;
using Grand.Domain.Seo;
using Grand.Infrastructure.Mapper;
using Microsoft.AspNetCore.StaticFiles;

namespace Grand.Business.Catalog.Services.ExportImport
{
    public class CollectionImportDataObject : IImportDataObject<CollectionDto>
    {
        private readonly ICollectionService _collectionService;
        private readonly IPictureService _pictureService;
        private readonly ICollectionLayoutService _collectionLayoutService;
        private readonly ISlugService _slugService;
        private readonly ILanguageService _languageService;

        private readonly SeoSettings _seoSetting;

        public CollectionImportDataObject(
            ICollectionService collectionService,
            IPictureService pictureService,
            ICollectionLayoutService collectionLayoutService,
            ISlugService slugService,
            ILanguageService languageService,
            SeoSettings seoSetting)
        {
            _collectionService = collectionService;
            _pictureService = pictureService;
            _collectionLayoutService = collectionLayoutService;
            _slugService = slugService;
            _languageService = languageService;

            _seoSetting = seoSetting;
        }

        public async Task Execute(IEnumerable<CollectionDto> data)
        {
            foreach (var item in data)
            {
                await Import(item);
            }
        }

        protected async Task Import(CollectionDto collectionDto)
        {
            var collection = await _collectionService.GetCollectionById(collectionDto.Id);
            var isNew = collection == null;

            if (collection == null) collection = collectionDto.MapTo<CollectionDto, Collection>();
            else collectionDto.MapTo(collection);

            if (!ValidCollection(collection)) return;

            if (isNew) await _collectionService.InsertCollection(collection);
            else await _collectionService.UpdateCollection(collection);

            await UpdateCollectionData(collectionDto, collection);
        }

        protected async Task<Collection> UpdateCollectionData(CollectionDto collectionDto, Collection collection)
        {
            if (string.IsNullOrEmpty(collection.CollectionLayoutId))
                collection.CollectionLayoutId = (await _collectionLayoutService.GetAllCollectionLayouts()).FirstOrDefault().Id;
            else
            {
                var layout = await _collectionLayoutService.GetCollectionLayoutById(collection.CollectionLayoutId);
                if (layout == null)
                    collection.CollectionLayoutId = (await _collectionLayoutService.GetAllCollectionLayouts()).FirstOrDefault().Id;
            }

            if (!string.IsNullOrEmpty(collectionDto.Picture))
            {
                var _picture = await LoadPicture(collectionDto.Picture, collection.Name, collection.PictureId);
                if (_picture != null)
                    collection.PictureId = _picture.Id;
            }

            var sename = collection.SeName ?? collection.Name;
            sename = await collection.ValidateSeName(sename, collection.Name, true, _seoSetting, _slugService, _languageService);
            collection.SeName = sename;

            await _collectionService.UpdateCollection(collection);
            await _slugService.SaveSlug(collection, sename, "");

            return collection;
        }

        protected virtual bool ValidCollection(Collection collection)
        {
            if (string.IsNullOrEmpty(collection.Name))
                return false;

            return true;
        }

        /// <summary>
        /// Creates or loads the image
        /// </summary>
        /// <param name="picturePath">The path to the image file</param>
        /// <param name="name">The name of the object</param>
        /// <param name="picId">Image identifier, may be null</param>
        /// <returns>The image or null if the image has not changed</returns>
        protected virtual async Task<Picture> LoadPicture(string picturePath, string name, string picId = "")
        {
            if (string.IsNullOrEmpty(picturePath) || !File.Exists(picturePath))
                return null;

            var mimeType = GetMimeTypeFromFilePath(picturePath);
            var newPictureBinary = File.ReadAllBytes(picturePath);
            var pictureAlreadyExists = false;
            if (!string.IsNullOrEmpty(picId))
            {
                //compare with existing product pictures
                var existingPicture = await _pictureService.GetPictureById(picId);

                var existingBinary = await _pictureService.LoadPictureBinary(existingPicture);
                //picture binary after validation (like in database)
                var validatedPictureBinary = _pictureService.ValidatePicture(newPictureBinary, mimeType);
                if (existingBinary.SequenceEqual(validatedPictureBinary) ||
                    existingBinary.SequenceEqual(newPictureBinary))
                {
                    pictureAlreadyExists = true;
                }
            }

            if (pictureAlreadyExists) return null;

            var newPicture = await _pictureService.InsertPicture(newPictureBinary, mimeType,
                _pictureService.GetPictureSeName(name));
            return newPicture;
        }
        protected virtual string GetMimeTypeFromFilePath(string filePath)
        {
            new FileExtensionContentTypeProvider().TryGetContentType(filePath, out string mimeType);
            //set to jpeg in case mime type cannot be found
            if (mimeType == null)
                mimeType = "image/jpeg";
            return mimeType;
        }
    }
}
