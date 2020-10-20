import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { executeCommand } from 'Store/Actions/commandActions';
import createExecutingCommandsSelector from 'Store/Selectors/createExecutingCommandsSelector';
import createIndexerSelector from 'Store/Selectors/createIndexerSelector';

function selectShowSearchAction() {
  return createSelector(
    (state) => state.indexerIndex,
    (indexerIndex) => {
      return indexerIndex.tableOptions.showSearchAction;
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    createIndexerSelector(),
    selectShowSearchAction(),
    createExecutingCommandsSelector(),
    (
      movie,
      showSearchAction,
      executingCommands
    ) => {

      // If a movie is deleted this selector may fire before the parent
      // selecors, which will result in an undefined movie, if that happens
      // we want to return early here and again in the render function to avoid
      // trying to show a movie that has no information available.

      if (!movie) {
        return {};
      }

      const isRefreshingMovie = executingCommands.some((command) => {
        return (
          command.name === commandNames.REFRESH_MOVIE &&
          command.body.movieIds.includes(movie.id)
        );
      });

      const isSearchingMovie = executingCommands.some((command) => {
        return (
          command.name === commandNames.MOVIE_SEARCH &&
          command.body.movieIds.includes(movie.id)
        );
      });

      return {
        ...movie,
        showSearchAction,
        isRefreshingMovie,
        isSearchingMovie
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchExecuteCommand: executeCommand
};

class MovieIndexItemConnector extends Component {

  //
  // Listeners

  onRefreshMoviePress = () => {
    this.props.dispatchExecuteCommand({
      name: commandNames.REFRESH_MOVIE,
      movieIds: [this.props.id]
    });
  }

  onSearchPress = () => {
    this.props.dispatchExecuteCommand({
      name: commandNames.MOVIE_SEARCH,
      movieIds: [this.props.id]
    });
  }

  //
  // Render

  render() {
    const {
      id,
      component: ItemComponent,
      ...otherProps
    } = this.props;

    if (!id) {
      return null;
    }

    return (
      <ItemComponent
        {...otherProps}
        id={id}
        onRefreshMoviePress={this.onRefreshMoviePress}
        onSearchPress={this.onSearchPress}
      />
    );
  }
}

MovieIndexItemConnector.propTypes = {
  id: PropTypes.number,
  component: PropTypes.elementType.isRequired,
  dispatchExecuteCommand: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(MovieIndexItemConnector);
