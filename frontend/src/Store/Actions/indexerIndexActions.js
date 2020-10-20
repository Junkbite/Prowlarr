import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { filterBuilderTypes, filterBuilderValueTypes, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import { set, updateItem } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { filterPredicates, filters, sortPredicates } from './movieActions';

//
// Variables

export const section = 'indexerIndex';

//
// State

export const defaultState = {
  isSaving: false,
  saveError: null,
  isDeleting: false,
  deleteError: null,
  sortKey: 'sortTitle',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'sortTitle',
  secondarySortDirection: sortDirections.ASCENDING,

  tableOptions: {
    showSearchAction: false
  },

  columns: [
    {
      name: 'select',
      columnLabel: 'Select',
      isSortable: false,
      isVisible: true,
      isModifiable: false,
      isHidden: true
    },
    {
      name: 'status',
      columnLabel: translate('ReleaseStatus'),
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'name',
      label: 'Indexer Name',
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'protocol',
      label: translate('Protocol'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'privacy',
      label: translate('Privacy'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'added',
      label: translate('Added'),
      isSortable: true,
      isVisible: false
    },
    {
      name: 'capabilities',
      label: 'Capabilities',
      isSortable: false,
      isVisible: false
    },
    {
      name: 'tags',
      label: translate('Tags'),
      isSortable: false,
      isVisible: false
    },
    {
      name: 'actions',
      columnLabel: translate('Actions'),
      isVisible: true,
      isModifiable: false
    }
  ],

  sortPredicates: {
    ...sortPredicates,

    studio: function(item) {
      const studio = item.studio;

      return studio ? studio.toLowerCase() : '';
    },

    collection: function(item) {
      const { collection ={} } = item;

      return collection.name;
    },

    ratings: function(item) {
      const { ratings = {} } = item;

      return ratings.value;
    }
  },

  selectedFilterKey: 'all',

  filters,
  filterPredicates,

  filterBuilderProps: [
    {
      name: 'monitored',
      label: translate('Monitored'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    },
    {
      name: 'title',
      label: 'Indexer Name',
      type: filterBuilderTypes.STRING
    },
    {
      name: 'added',
      label: translate('Added'),
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'tags',
      label: translate('Tags'),
      type: filterBuilderTypes.ARRAY,
      valueType: filterBuilderValueTypes.TAG
    }
  ]
};

export const persistState = [
  'indexerIndex.sortKey',
  'indexerIndex.sortDirection',
  'indexerIndex.selectedFilterKey',
  'indexerIndex.customFilters',
  'indexerIndex.view',
  'indexerIndex.columns',
  'indexerIndex.tableOptions'
];

//
// Actions Types

export const SET_MOVIE_SORT = 'indexerIndex/setMovieSort';
export const SET_MOVIE_FILTER = 'indexerIndex/setMovieFilter';
export const SET_MOVIE_VIEW = 'indexerIndex/setMovieView';
export const SET_MOVIE_TABLE_OPTION = 'indexerIndex/setMovieTableOption';
export const SAVE_MOVIE_EDITOR = 'indexerIndex/saveMovieEditor';
export const BULK_DELETE_MOVIE = 'indexerIndex/bulkDeleteMovie';

//
// Action Creators

export const setMovieSort = createAction(SET_MOVIE_SORT);
export const setMovieFilter = createAction(SET_MOVIE_FILTER);
export const setMovieView = createAction(SET_MOVIE_VIEW);
export const setMovieTableOption = createAction(SET_MOVIE_TABLE_OPTION);
export const saveMovieEditor = createThunk(SAVE_MOVIE_EDITOR);
export const bulkDeleteMovie = createThunk(BULK_DELETE_MOVIE);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [SAVE_MOVIE_EDITOR]: function(getState, payload, dispatch) {
    dispatch(set({
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: '/movie/editor',
      method: 'PUT',
      data: JSON.stringify(payload),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        ...data.map((movie) => {
          return updateItem({
            id: movie.id,
            section: 'movies',
            ...movie
          });
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [BULK_DELETE_MOVIE]: function(getState, payload, dispatch) {
    dispatch(set({
      section,
      isDeleting: true
    }));

    const promise = createAjaxRequest({
      url: '/movie/editor',
      method: 'DELETE',
      data: JSON.stringify(payload),
      dataType: 'json'
    }).request;

    promise.done(() => {
      // SignaR will take care of removing the movie from the collection

      dispatch(set({
        section,
        isDeleting: false,
        deleteError: null
      }));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isDeleting: false,
        deleteError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_MOVIE_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_MOVIE_FILTER]: createSetClientSideCollectionFilterReducer(section),

  [SET_MOVIE_VIEW]: function(state, { payload }) {
    return Object.assign({}, state, { view: payload.view });
  },

  [SET_MOVIE_TABLE_OPTION]: createSetTableOptionReducer(section)

}, defaultState, section);
